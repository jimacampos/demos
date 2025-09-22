using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.WebApi;


namespace AdoAgentDemo
{
    /// <summary>
    /// Thin wrapper over Azure DevOps .NET SDK for querying pipelines (builds),
    /// releases, and deployments. Authenticates with a PAT.
    /// </summary>
    public sealed class AzureDevOpsClient : IAsyncDisposable
    {
        private readonly Uri _orgUrl;
        private readonly string _project;
        private readonly VssConnection _connection;
        private readonly BuildHttpClient _builds;
        private readonly ReleaseHttpClient _releases;

        /// <param name="organizationUrl">e.g., https://dev.azure.com/yourorg</param>
        /// <param name="project">Default project name or ID</param>
        /// <param name="personalAccessToken">PAT with Build (Read) and Release (Read) scopes as needed</param>
        public AzureDevOpsClient(string organizationUrl, string project, string personalAccessToken)
        {
            if (string.IsNullOrWhiteSpace(organizationUrl)) throw new ArgumentNullException(nameof(organizationUrl));
            if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(personalAccessToken)) throw new ArgumentNullException(nameof(personalAccessToken));

            _orgUrl = new Uri(organizationUrl);
            _project = project;

            var creds = new VssBasicCredential(string.Empty, personalAccessToken);
            _connection = new VssConnection(_orgUrl, creds);

            // Http clients (dispose on close)
            _builds = _connection.GetClient<BuildHttpClient>();
            _releases = _connection.GetClient<ReleaseHttpClient>();
        }

        #region Pipelines (Builds)

        /// <summary>
        /// List build definitions (i.e., pipeline definitions) in the project.
        /// </summary>
        public async Task<IReadOnlyList<PipelineDefinitionInfo>> GetBuildDefinitionsAsync(
            string? nameFilter = null,
            CancellationToken ct = default)
        {
            var defs = await _builds.GetDefinitionsAsync(
                project: _project,
                name: nameFilter,
                cancellationToken: ct);

            return defs.Select(d => new PipelineDefinitionInfo
            {
                Id = d.Id,
                Name = d.Name ?? $"Definition {d.Id}",
                Path = d.Path,
                QueueStatus = d.QueueStatus.ToString(),
                Revision = d.Revision.Value
            }).ToList();
        }

        /// <summary>
        /// Get most-recent builds for the project (optionally by definition).
        /// </summary>
        public async Task<IReadOnlyList<BuildRunInfo>> GetLatestBuildsAsync(
            int top = 20,
            int? definitionId = null,
            BuildResult? resultFilter = null,
            CancellationToken ct = default)
        {
            var builds = await _builds.GetBuildsAsync(
                project: _project,
                definitions: definitionId is null ? null : new[] { definitionId.Value },
                resultFilter: resultFilter,
                top: top,
                queryOrder: BuildQueryOrder.FinishTimeDescending,
                cancellationToken: ct);

            return builds.Select(MapBuild).ToList();
        }

        /// <summary>
        /// Get a single build by id.
        /// </summary>
        public async Task<BuildRunInfo?> GetBuildAsync(
            int buildId,
            CancellationToken ct = default)
        {
            var b = await _builds.GetBuildAsync(_project, buildId);
            return b is null ? null : MapBuild(b);
        }

        /// <summary>
        /// Get simple “latest status” signal for a definition: last finished build’s status/result.
        /// </summary>
        public async Task<LatestStatus?> GetLatestBuildStatusAsync(
            int definitionId,
            CancellationToken ct = default)
        {
            var latest = await _builds.GetBuildsAsync(
                project: _project,
                definitions: new[] { definitionId },
                statusFilter: BuildStatus.Completed,
                queryOrder: BuildQueryOrder.FinishTimeDescending,
                top: 1,
                cancellationToken: ct);

            var b = latest.FirstOrDefault();
            if (b == null) return null;

            return new LatestStatus
            {
                DefinitionId = definitionId,
                DefinitionName = b.Definition?.Name ?? $"Definition {definitionId}",
                BuildId = b.Id,
                Status = b.Status?.ToString() ?? "Unknown",
                Result = b.Result?.ToString() ?? "Unknown",
                FinishTimeUtc = b.FinishTime?.ToUniversalTime()
            };
        }

        private static BuildRunInfo MapBuild(Build b) => new BuildRunInfo
        {
            Id = b.Id,
            DefinitionId = b.Definition?.Id ?? 0,
            DefinitionName = b.Definition?.Name ?? $"Definition {b.Definition?.Id}",
            BuildNumber = b.BuildNumber,
            SourceBranch = b.SourceBranch,
            RequestedBy = b.RequestedBy?.DisplayName,
            QueueTimeUtc = b.QueueTime?.ToUniversalTime(),
            StartTimeUtc = b.StartTime?.ToUniversalTime(),
            FinishTimeUtc = b.FinishTime?.ToUniversalTime(),
            Status = b.Status?.ToString() ?? "Unknown",
            Result = b.Result?.ToString() ?? "Unknown",
            WebLink = b.Links.Links?["web"] is ReferenceLink link ? link.Href : null
        };

        #endregion

        #region Releases & Deployments (Classic Release)

        /// <summary>
        /// List releases (classic Release pipelines). Use filters to constrain if desired.
        /// </summary>
        public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(
            int top = 20,
            int? definitionId = null,
            CancellationToken ct = default)
        {
            var releases = await _releases.GetReleasesAsync(
                project: _project,
                definitionId: definitionId,
                top: top,
                queryOrder: ReleaseQueryOrder.Descending,
                cancellationToken: ct);

            return releases.Select(r => new ReleaseInfo
            {
                Id = r.Id,
                Name = r.Name,
                DefinitionId = r.ReleaseDefinition?.Id ?? 0,
                DefinitionName = r.ReleaseDefinition?.Name ?? $"Definition {r.ReleaseDefinition?.Id}",
                CreatedOnUtc = r.CreatedOn.ToUniversalTime(),
                CreatedBy = r.CreatedBy?.DisplayName,
                Status = r.Status.ToString(),
                Environments = r.Environments?.Select(e => new ReleaseEnvInfo
                {
                    Id = e.Id,
                    Name = e.Name,
                    Rank = e.Rank,
                    Status = e.Status.ToString(),
                    Conditions = e.Conditions?.Select(c => $"{c.ConditionType}:{c.Name}={c.Value}").ToList() ?? new List<string>()
                }).ToList() ?? new List<ReleaseEnvInfo>(),
                WebLink = r.Links.Links?["web"] is ReferenceLink link ? link.Href : null
            }).ToList();
        }

        /// <summary>
        /// Get a single release.
        /// </summary>
        public async Task<ReleaseInfo?> GetReleaseAsync(
            int releaseId,
            CancellationToken ct = default)
        {
            var r = await _releases.GetReleaseAsync(_project, releaseId);
            if (r == null) return null;
            return (await GetReleasesAsync(top: 1, definitionId: null, ct: ct))
                .FirstOrDefault(x => x.Id == releaseId) ?? // fast path if cached
                   new ReleaseInfo
                   {
                       Id = r.Id,
                       Name = r.Name,
                       DefinitionId = r.ReleaseDefinition?.Id ?? 0,
                       DefinitionName = r.ReleaseDefinition?.Name ?? $"Definition {r.ReleaseDefinition?.Id}",
                       CreatedOnUtc = r.CreatedOn.ToUniversalTime(),
                       CreatedBy = r.CreatedBy?.DisplayName,
                       Status = r.Status.ToString(),
                       Environments = r.Environments?.Select(e => new ReleaseEnvInfo
                       {
                           Id = e.Id,
                           Name = e.Name,
                           Rank = e.Rank,
                           Status = e.Status.ToString(),
                           Conditions = e.Conditions?.Select(c => $"{c.ConditionType}:{c.Name}={c.Value}").ToList() ?? new List<string>()
                       }).ToList() ?? new List<ReleaseEnvInfo>(),
                       WebLink = r.Links.Links?["web"] is ReferenceLink link ? link.Href : null
                   };
        }

        /// <summary>
        /// List deployments (classic release) for an environment or definition.
        /// </summary>
        public async Task<IReadOnlyList<DeploymentInfo>> GetDeploymentsAsync(
            int? definitionId = null,
            int? environmentId = null,
            DeploymentStatus? statusFilter = null,
            int top = 50,
            CancellationToken ct = default)
        {
            var deployments = await _releases.GetDeploymentsAsync(
                project: _project,
                definitionId: definitionId,
                definitionEnvironmentId: environmentId,
                deploymentStatus: statusFilter,
                top: top,
                queryOrder: ReleaseQueryOrder.Descending,
                cancellationToken: ct);

            return deployments.Select(d => new DeploymentInfo
            {
                Id = d.Id,
                ReleaseId = d.Release?.Id ?? 0,
                ReleaseName = d.Release?.Name,
                DefinitionId = d.ReleaseDefinition?.Id ?? 0,
                DefinitionName = d.ReleaseDefinition?.Name,
                EnvironmentId = d.ReleaseEnvironment?.Id ?? 0,
                EnvironmentName = d.ReleaseEnvironment?.Name,
                Status = d.DeploymentStatus.ToString(),
                Attempt = d.Attempt,
                StartedOnUtc = d.StartedOn.ToUniversalTime(),
                CompletedOnUtc = d.CompletedOn.ToUniversalTime(),
                RequestedBy = d.RequestedBy?.DisplayName,
                RequestedFor = d.RequestedFor?.DisplayName,
                // WebLink = d.Links.Links?["web"] is ReferenceLink link ? link.Href : null
            }).ToList();
        }

        #endregion

        public async ValueTask DisposeAsync()
        {
            // Dispose HTTP clients & connection
            _builds?.Dispose();
            _releases?.Dispose();
            _connection?.Dispose();
            await Task.CompletedTask;
        }
    }

    #region Models

    public sealed class PipelineDefinitionInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public string QueueStatus { get; set; } = "Unknown";
        public int Revision { get; set; }
    }

    public sealed class BuildRunInfo
    {
        public int Id { get; set; }
        public int DefinitionId { get; set; }
        public string DefinitionName { get; set; } = "";
        public string? BuildNumber { get; set; }
        public string? SourceBranch { get; set; }
        public string? RequestedBy { get; set; }
        public DateTimeOffset? QueueTimeUtc { get; set; }
        public DateTimeOffset? StartTimeUtc { get; set; }
        public DateTimeOffset? FinishTimeUtc { get; set; }
        public string Status { get; set; } = "Unknown";
        public string Result { get; set; } = "Unknown";
        public string? WebLink { get; set; }
    }

    public sealed class LatestStatus
    {
        public int DefinitionId { get; set; }
        public string DefinitionName { get; set; } = "";
        public int BuildId { get; set; }
        public string Status { get; set; } = "Unknown";
        public string Result { get; set; } = "Unknown";
        public DateTimeOffset? FinishTimeUtc { get; set; }
    }

    public sealed class ReleaseInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int DefinitionId { get; set; }
        public string DefinitionName { get; set; } = "";
        public DateTimeOffset CreatedOnUtc { get; set; }
        public string? CreatedBy { get; set; }
        public string Status { get; set; } = "Unknown";
        public List<ReleaseEnvInfo> Environments { get; set; } = new();
        public string? WebLink { get; set; }
    }

    public sealed class ReleaseEnvInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Rank { get; set; }
        public string Status { get; set; } = "Unknown";
        public List<string> Conditions { get; set; } = new();
    }

    public sealed class DeploymentInfo
    {
        public int Id { get; set; }
        public int ReleaseId { get; set; }
        public string? ReleaseName { get; set; }
        public int DefinitionId { get; set; }
        public string? DefinitionName { get; set; }
        public int EnvironmentId { get; set; }
        public string? EnvironmentName { get; set; }
        public string Status { get; set; } = "Unknown";
        public int Attempt { get; set; }
        public DateTimeOffset? StartedOnUtc { get; set; }
        public DateTimeOffset? CompletedOnUtc { get; set; }
        public string? RequestedBy { get; set; }
        public string? RequestedFor { get; set; }
        public string? WebLink { get; set; }
    }

    #endregion
}
