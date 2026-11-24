using System.Windows.Input;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class MutationAction : IMutationComponent, ICommand
{
    private readonly Action _execute;

    public MutationAction(string name, Action execute)
    {
        Name = name;
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public string Name { get; }

    public bool IsDeveloper { get; set; }

    public string? Group { get; set; }

    public bool StartsGroup { get; set; }

    public MutationMetadata? Metadata { get; set; }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _ = RunAsync(CancellationToken.None);

    public Task RunAsync(CancellationToken cancellationToken) => Task.Run(_execute, cancellationToken);
}
