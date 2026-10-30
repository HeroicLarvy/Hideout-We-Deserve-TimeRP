using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SemanticVersioning;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Web;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace HideoutWeDeserveTimeRP;

public sealed class HideoutWeDeserveTimeRPMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.garon.hideoutwedeservetimerp";
    public string Name { get; init; } = "Hideout We Deserve - TimeRP";
    public string Author { get; init; } = "HeroicLarvy";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new("1.4.0");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public string? WWWRootUrl { get; init; } = "HideoutWeDeserveTimeRP";
    public string? HomePage { get; init; } = "/HWDTimeRP";
    public string? HomePageDescription { get; init; } = "Edit Hideout We Deserve - TimeRP settings";
}

public sealed class RaidTimeConfig
{
    [JsonPropertyName("baseTimeMultiplier")]
    public double BaseTimeMultiplier { get; init; }

    [JsonPropertyName("dangerThresholdMinutes")]
    public double DangerThresholdMinutes { get; init; }

    [JsonPropertyName("defaultDangerMultiplier")]
    public double DefaultDangerMultiplier { get; init; }

    [JsonPropertyName("mapDangerMultipliers")]
    public Dictionary<string, double> MapDangerMultipliers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("debug")]
    public bool Debug { get; init; }
}

public sealed record HideoutWeDeserveTimeRPProgressionResult(
    [property: JsonPropertyName("raidMinutes")] double RaidMinutes,
    [property: JsonPropertyName("progressMinutes")] double ProgressMinutes,
    [property: JsonPropertyName("craftsAdvanced")] int CraftsAdvanced,
    [property: JsonPropertyName("upgradesAdvanced")] int UpgradesAdvanced,
    [property: JsonPropertyName("mapId")] string MapId
);

public sealed record HideoutWeDeserveTimeRPTestRequest : IRequestData
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; init; } = string.Empty;
}

public sealed record HideoutWeDeserveTimeRPProfileOption(
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("username")] string Username
);

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class HideoutWeDeserveTimeRPMod(
    ISptLogger<HideoutWeDeserveTimeRPMod> logger,
    ProfileHelper profileHelper,
    SaveServer saveServer
) : IOnLoad
{
    private static HideoutWeDeserveTimeRPMod? _instance;
    private static Harmony? _harmony;
    private RaidTimeConfig _config = new();
    private string? _configPath;

    public static HideoutWeDeserveTimeRPMod Instance =>
        _instance ?? throw new InvalidOperationException("Hideout We Deserve - TimeRP was not initialized.");

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Could not resolve the Hideout We Deserve - TimeRP mod directory.");
        _configPath = Path.Combine(modDirectory, "config.json");
        _config = LoadConfig(_configPath);

        _instance = this;
        _harmony = new Harmony("com.garon.hideoutwedeservetimerp");
        MethodInfo endLocalRaidMethod = AccessTools.Method(
            typeof(LocationLifecycleService),
            nameof(LocationLifecycleService.EndLocalRaidAsync));
        if (endLocalRaidMethod is null)
        {
            throw new MissingMethodException(
                typeof(LocationLifecycleService).FullName,
                nameof(LocationLifecycleService.EndLocalRaidAsync));
        }

        _harmony.Patch(
            endLocalRaidMethod,
            postfix: new HarmonyMethod(typeof(HideoutWeDeserveTimeRPMod), nameof(AfterEndLocalRaid)));

        logger.Info(
            $"Hideout We Deserve - TimeRP loaded: { _config.BaseTimeMultiplier:0.##}x base, " +
            $"map danger multipliers after {_config.DangerThresholdMinutes:0.##} minutes.");
        return Task.CompletedTask;
    }

    private static RaidTimeConfig LoadConfig(string path)
    {
        RaidTimeConfig? config = JsonSerializer.Deserialize<RaidTimeConfig>(File.ReadAllText(path));
        if (config is null)
        {
            throw new InvalidDataException($"Invalid Hideout We Deserve - TimeRP configuration: {path}");
        }

        ValidateConfig(config);
        return config;
    }

    private static void ValidateConfig(RaidTimeConfig config)
    {
        if (
            !double.IsFinite(config.BaseTimeMultiplier) ||
            config.BaseTimeMultiplier <= 0 ||
            !double.IsFinite(config.DangerThresholdMinutes) ||
            config.DangerThresholdMinutes < 0 ||
            !double.IsFinite(config.DefaultDangerMultiplier) ||
            config.DefaultDangerMultiplier < 0 ||
            config.MapDangerMultipliers is null ||
            config.MapDangerMultipliers.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                !double.IsFinite(pair.Value) ||
                pair.Value < 0)
        )
        {
            throw new InvalidDataException("The base multiplier must be positive. All config values must be finite, and danger multipliers cannot be negative.");
        }
    }

    public RaidTimeConfig GetConfig()
    {
        return CloneConfig(_config);
    }

    public bool DebugEnabled => _config.Debug;

    public RaidTimeConfig SaveConfig(RaidTimeConfig config)
    {
        ValidateConfig(config);

        string configPath = _configPath
            ?? throw new InvalidOperationException("Hideout We Deserve - TimeRP configuration has not been initialized.");
        RaidTimeConfig savedConfig = CloneConfig(config);
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(configPath)
                ?? throw new InvalidOperationException("Could not resolve the Hideout We Deserve - TimeRP mod directory."),
            $"config.{Guid.NewGuid():N}.tmp"
        );

        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(savedConfig, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
            File.Move(temporaryPath, configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        _config = savedConfig;
        logger.Info("Hideout We Deserve - TimeRP configuration was updated from the web UI.");
        return CloneConfig(savedConfig);
    }

    private static RaidTimeConfig CloneConfig(RaidTimeConfig config)
    {
        return new RaidTimeConfig
        {
            BaseTimeMultiplier = config.BaseTimeMultiplier,
            DangerThresholdMinutes = config.DangerThresholdMinutes,
            DefaultDangerMultiplier = config.DefaultDangerMultiplier,
            Debug = config.Debug,
            MapDangerMultipliers = new Dictionary<string, double>(
                config.MapDangerMultipliers,
                StringComparer.OrdinalIgnoreCase
            ),
        };
    }

    private static void AfterEndLocalRaid(
        ref Task __result,
        MongoId sessionId,
        EndLocalRaidRequestData request
    )
    {
        HideoutWeDeserveTimeRPMod instance = _instance
            ?? throw new InvalidOperationException("Hideout We Deserve - TimeRP was not initialized.");
        __result = instance.ApplyAfterRaidAsync(__result, sessionId, request);
    }

    private async Task ApplyAfterRaidAsync(
        Task originalTask,
        MongoId sessionId,
        EndLocalRaidRequestData request
    )
    {
        await originalTask.ConfigureAwait(false);

        EndRaidResult? result = request.Results;
        if (!string.Equals(result?.Result?.ToString(), "Survived", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        double? raidSeconds = result?.PlayTime;
        if (raidSeconds is not double duration || !double.IsFinite(duration) || duration <= 0)
        {
            logger.Warning("Hideout We Deserve - TimeRP could not advance timers: SPT provided no valid raid duration.");
            return;
        }

        string? serverId = request.ServerId;
        string[] serverDetails = serverId?.Split('.') ?? [];
        if (serverDetails.Length < 2)
        {
            logger.Warning($"Hideout We Deserve - TimeRP could not identify the player or map from raid server ID '{serverId}'.");
            return;
        }

        bool isScav =
            serverDetails[1].Contains("scav", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(serverDetails[1], "savage", StringComparison.OrdinalIgnoreCase);
        bool isPmc = serverDetails[1].Contains("pmc", StringComparison.OrdinalIgnoreCase);
        if (!isScav && !isPmc)
        {
            logger.Warning($"Hideout We Deserve - TimeRP ignored an unsupported raid type in server ID '{serverId}'.");
            return;
        }

        string mapId = serverDetails[0].Trim().ToLowerInvariant();
        if (mapId.Length == 0)
        {
            logger.Warning("Hideout We Deserve - TimeRP could not identify the map for a survived raid.");
            return;
        }

        string mapKey = NormalizeMapId(mapId);
        string playerType = isScav ? "scav" : "PMC";
        logger.Info($"Applying survived {playerType} raid progression on {mapId} for {duration:0} seconds.");
        await ApplyRaidProgressionAsync(sessionId, mapKey, duration);
    }

    public Task<HideoutWeDeserveTimeRPProgressionResult> SimulateRaidProgressionAsync(
        MongoId sessionId,
        string mapId,
        double raidSeconds
    )
    {
        return ApplyRaidProgressionAsync(sessionId, NormalizeMapId(mapId), raidSeconds);
    }

    private async Task<HideoutWeDeserveTimeRPProgressionResult> ApplyRaidProgressionAsync(
        MongoId sessionId,
        string mapId,
        double duration
    )
    {
        if (!double.IsFinite(duration) || duration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Raid duration must be positive and finite.");
        }

        double dangerMultiplier = _config.MapDangerMultipliers.TryGetValue(mapId, out double configuredMultiplier)
            ? configuredMultiplier
            : _config.DefaultDangerMultiplier;
        double thresholdSeconds = _config.DangerThresholdMinutes * 60;
        double baseSeconds = Math.Min(duration, thresholdSeconds) * _config.BaseTimeMultiplier;
        double dangerSeconds = Math.Max(0, duration - thresholdSeconds) *
            _config.BaseTimeMultiplier *
            dangerMultiplier;
        long progressSeconds = checked((long)Math.Round(baseSeconds + dangerSeconds, MidpointRounding.AwayFromZero));
        if (progressSeconds <= 0)
        {
            return new HideoutWeDeserveTimeRPProgressionResult(duration / 60, 0, 0, 0, mapId);
        }

        var pmcProfile = profileHelper.GetPmcProfile(sessionId);
        var hideout = pmcProfile?.Hideout;
        if (hideout is null)
        {
            throw new InvalidOperationException("Could not find the PMC hideout for the selected profile.");
        }

        int craftsAdvanced = 0;
        foreach (
            SPTarkov.Server.Core.Models.Eft.Common.Tables.Production? production
            in hideout.Production?.Values ?? Enumerable.Empty<SPTarkov.Server.Core.Models.Eft.Common.Tables.Production?>()
        )
        {
            if (production?.InProgress != true || production.Interrupted == true)
            {
                continue;
            }

            if (production.Progress is not double currentProgress || !double.IsFinite(currentProgress))
            {
                logger.Warning(
                    $"Hideout We Deserve - TimeRP could not advance craft {production.RecipeId}: its progress value is missing or invalid.");
                continue;
            }

            production.Progress = currentProgress + progressSeconds;
            craftsAdvanced++;
            logger.Debug(
                $"Advanced craft {production.RecipeId} progress from {currentProgress:0.##} to {production.Progress:0.##} seconds.");
        }

        int upgradesAdvanced = 0;
        foreach (var area in hideout.Areas ?? [])
        {
            if (area.Constructing == true && area.CompleteTime is int completeTime)
            {
                area.CompleteTime = checked((int)(completeTime - progressSeconds));
                upgradesAdvanced++;
            }
        }

        await saveServer.SaveProfileAsync(sessionId);

        var result = new HideoutWeDeserveTimeRPProgressionResult(
            duration / 60,
            progressSeconds / 60.0,
            craftsAdvanced,
            upgradesAdvanced,
            mapId
        );
        logger.Info(
            $"{mapId} raid progression for {duration / 60:0.0} minutes: advanced and saved hideout timers by " +
            $"{progressSeconds / 60.0:0.0} minutes ({craftsAdvanced} craft progress values, {upgradesAdvanced} upgrades; " +
            $"danger {dangerMultiplier:0.##}x).");
        return result;
    }

    private static string NormalizeMapId(string mapId)
    {
        return mapId switch
        {
            "factory4_day" or "factory4_night" => "factory",
            "laboratory" => "labs",
            "sandbox" or "sandbox_high" => "ground_zero",
            "bigmap" => "customs",
            "rezervbase" => "reserve",
            "tarkovstreets" => "streets",
            _ => mapId,
        };
    }
}

[ApiController]
[Authorize]
[Route("HWDTimeRP")]
public sealed class HideoutWeDeserveTimeRPTestController(
    SaveServer saveServer,
    ISptLogger<HideoutWeDeserveTimeRPTestController> logger
) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        string modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Could not resolve the Hideout We Deserve - TimeRP mod directory.");
        string pagePath = Path.Combine(modDirectory, "wwwroot", "index.html");
        return PhysicalFile(pagePath, "text/html; charset=utf-8");
    }

    [HttpGet("api/config")]
    public ActionResult<RaidTimeConfig> GetConfig()
    {
        return Ok(HideoutWeDeserveTimeRPMod.Instance.GetConfig());
    }

    [HttpPut("api/config")]
    public ActionResult<RaidTimeConfig> SaveConfig([FromBody] RaidTimeConfig? config)
    {
        if (config is null)
        {
            return BadRequest(new { error = "A config object is required." });
        }

        try
        {
            return Ok(HideoutWeDeserveTimeRPMod.Instance.SaveConfig(config));
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("api/profiles")]
    public ActionResult<HideoutWeDeserveTimeRPProfileOption[]> GetProfiles()
    {
        var profiles = saveServer.GetProfiles()
            .Select(profile => new HideoutWeDeserveTimeRPProfileOption(
                profile.Key.ToString(),
                profile.Value.ProfileInfo?.Username ?? profile.Key.ToString()
            ))
            .OrderBy(profile => profile.Username, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        logger.Info($"Hideout We Deserve - TimeRP web UI listed {profiles.Length} profiles.");
        return Ok(profiles);
    }

    [HttpPost("api/test/forty-seconds")]
    public Task<IActionResult> SimulateBaseProgressionAsync([FromBody] HideoutWeDeserveTimeRPTestRequest info)
    {
        return SimulateProgressionAsync(info, "factory", 40);
    }

    [HttpPost("api/test/woods-forty-minutes")]
    public Task<IActionResult> SimulateWoodsProgressionAsync([FromBody] HideoutWeDeserveTimeRPTestRequest info)
    {
        return SimulateProgressionAsync(info, "woods", 40 * 60);
    }

    private async Task<IActionResult> SimulateProgressionAsync(
        HideoutWeDeserveTimeRPTestRequest info,
        string mapId,
        double raidSeconds
    )
    {
        if (!HideoutWeDeserveTimeRPMod.Instance.DebugEnabled)
        {
            return NotFound();
        }

        if (
            info.ProfileId.Length != 24 ||
            info.ProfileId.Any(character => !Uri.IsHexDigit(character))
        )
        {
            return BadRequest(new { error = "A valid profile must be selected." });
        }

        var profileId = new MongoId(info.ProfileId);
        if (!saveServer.ProfileExists(profileId))
        {
            return NotFound(new { error = "The selected profile no longer exists." });
        }

        logger.Warning($"Web UI requested a real timer test for profile {profileId} on {mapId}.");
        HideoutWeDeserveTimeRPProgressionResult result =
            await HideoutWeDeserveTimeRPMod.Instance.SimulateRaidProgressionAsync(profileId, mapId, raidSeconds);
        return Ok(result);
    }
}
