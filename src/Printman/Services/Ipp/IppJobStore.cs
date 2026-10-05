using System.Collections.Concurrent;
using Printman.Core.Abstractions;
using Printman.Core.Models;

namespace Printman.Services.Ipp;

public class IppJobStore : IIppJobStore
{
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetainFinishedFor = TimeSpan.FromHours(1);
    private const int MaxFinishedJobs = 100;

    private readonly ConcurrentDictionary<int, IppJob> _jobs = new();
    private int _nextId;

    public IppJob Create(string printerSlug, string name, string userName, IppJobOptions options)
    {
        Sweep();
        var job = new IppJob
        {
            Id = Interlocked.Increment(ref _nextId),
            PrinterSlug = printerSlug,
            Name = name,
            UserName = userName,
            Options = options
        };
        _jobs[job.Id] = job;
        return job;
    }

    public IppJob? Get(int jobId)
    {
        Sweep();
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    public IReadOnlyList<IppJob> List(string printerSlug)
    {
        Sweep();
        return _jobs.Values
            .Where(j => j.PrinterSlug == printerSlug)
            .OrderBy(j => j.Id)
            .ToList();
    }

    public int ActiveCount(string printerSlug) =>
        _jobs.Values.Count(j => j.PrinterSlug == printerSlug && !IppJobState.IsTerminal(j.GetState().State));

    private void Sweep()
    {
        var now = DateTime.UtcNow;

        // Create-Job that never received its document
        foreach (var job in _jobs.Values)
        {
            if (job.AwaitingDocument && now - job.CreatedAt > AbandonedAfter)
            {
                job.Terminate(IppJobState.Aborted, "aborted-by-system");
            }
        }

        var finished = _jobs.Values
            .Where(j => IppJobState.IsTerminal(j.GetState().State))
            .OrderByDescending(j => j.Id)
            .ToList();

        for (int i = 0; i < finished.Count; i++)
        {
            var job = finished[i];
            if (i >= MaxFinishedJobs || (job.CompletedAt is DateTime done && now - done > RetainFinishedFor))
            {
                _jobs.TryRemove(job.Id, out _);
            }
        }
    }
}
