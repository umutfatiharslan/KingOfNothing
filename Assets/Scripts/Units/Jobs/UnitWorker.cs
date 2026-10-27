using System.Collections.Generic;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>State of a job after a tick.</summary>
    public enum JobState
    {
        Running,
        Done,
        Failed,
    }

    /// <summary>
    /// Something a character does on its own over time: walk to a tree and chop it, carry an object somewhere...
    /// </summary>
    public abstract class UnitJob
    {
        public abstract string Label { get; }

        /// <summary>Called once when the job becomes the current one.</summary>
        public virtual void Begin(Unit unit) { }

        /// <summary>Called every frame while it is the current job.</summary>
        public abstract JobState Tick(Unit unit, float deltaTime);

        /// <summary>The job is dropped before it finished (new command, the unit is gone...). Clean up here.</summary>
        public virtual void Cancel(Unit unit) { }
    }

    /// <summary>
    /// Runs the jobs of one character one after another (added automatically by Unit). A move order from the
    /// player clears the queue.
    /// </summary>
    [DisallowMultipleComponent]
    public class UnitWorker : MonoBehaviour
    {
        [Tooltip("Log job start / end in the console.")]
        [SerializeField] bool logJobs;

        readonly Queue<UnitJob> queue = new Queue<UnitJob>();
        UnitJob current;
        Unit unit;

        public UnitJob Current => current;
        public int QueuedCount => queue.Count + (current != null ? 1 : 0);
        public bool IsBusy => current != null || queue.Count > 0;

        void Awake() => unit = GetComponent<Unit>();

        /// <summary>Adds a job; without append every other job is cancelled first.</summary>
        public void Assign(UnitJob job, bool append = false)
        {
            if (job == null) return;
            if (!append) ClearJobs();
            queue.Enqueue(job);
        }

        public void ClearJobs()
        {
            if (current != null)
            {
                current.Cancel(unit);
                current = null;
            }
            queue.Clear();
        }

        void Update()
        {
            if (unit == null) return;
            if (current == null)
            {
                if (queue.Count == 0) return;
                current = queue.Dequeue();
                if (logJobs) Debug.Log($"[{name}] starts: {current.Label}", this);
                current.Begin(unit);
            }

            var state = current.Tick(unit, Time.deltaTime);
            if (state == JobState.Running) return;
            if (logJobs) Debug.Log($"[{name}] {(state == JobState.Done ? "finished" : "gave up")}: {current.Label}", this);
            current = null;
        }

        void OnDisable() => ClearJobs();
    }
}
