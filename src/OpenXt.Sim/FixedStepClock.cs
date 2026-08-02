namespace OpenXt.Sim;

/// <summary>
/// Fixed-timestep accumulator. The simulation always advances in identical slices regardless of
/// frame rate; rendering interpolates using <see cref="Alpha"/>. Nothing in the sim may read
/// wall-clock time.
/// </summary>
public sealed class FixedStepClock(float stepSeconds = 1f / 60f, int maxStepsPerFrame = 8)
{
    private float _accumulator;

    public float Step { get; } = stepSeconds;

    /// <summary>Spiral-of-death guard: beyond this the sim drops time rather than falling further behind.</summary>
    public int MaxStepsPerFrame { get; } = maxStepsPerFrame;

    /// <summary>Fraction into the next step, for render-side interpolation. Range [0, 1).</summary>
    public float Alpha => _accumulator / Step;

    /// <summary>Steps dropped because the frame exceeded <see cref="MaxStepsPerFrame"/>. Non-zero means trouble.</summary>
    public long DroppedSteps { get; private set; }

    /// <summary>Feeds real elapsed time in and returns how many fixed steps to run this frame.</summary>
    public int Advance(float elapsedSeconds)
    {
        _accumulator += elapsedSeconds;

        int steps = 0;
        while (_accumulator >= Step && steps < MaxStepsPerFrame)
        {
            _accumulator -= Step;
            steps++;
        }

        if (_accumulator >= Step)
        {
            long skipped = (long)(_accumulator / Step);
            DroppedSteps += skipped;
            _accumulator -= skipped * Step;
        }

        return steps;
    }
}
