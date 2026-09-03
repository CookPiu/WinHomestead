using System.Collections.Generic;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

public abstract class TaskBase : ITask
{
    public abstract TaskMetadata Metadata { get; }
    public virtual bool IsApplicable(EnvironmentSnapshot snapshot, Answers answers) => true;
    public virtual bool DefaultChecked(EnvironmentSnapshot snapshot, Answers answers) => true;
    public abstract DetectResult Detect(TaskContext ctx);
    public abstract void Apply(TaskContext ctx);
    public virtual bool Verify(TaskContext ctx) => Detect(ctx).Satisfied;
    public virtual void Rollback(TaskContext ctx, IReadOnlyList<JournalEntry> entries) => ctx.Undo(entries);

    protected static string DataRoot(TaskContext ctx) => (ctx.Answers.DataDrive ?? ctx.Snapshot.DataDrive ?? ctx.Snapshot.SystemDrive) + "\\";
    protected static bool HasDataDrive(EnvironmentSnapshot s, Answers a) => !string.IsNullOrEmpty(a.DataDrive ?? s.DataDrive);
}
