namespace Horyzont.Game.Client.Character;

/// <summary>Where <see cref="LocalPlayer"/> reads the input of one tick from; tests replace the keyboard and the mouse.</summary>
public interface ILocalInputSource
{
    /// <summary>Takes the input of this tick and resets the accumulated mouse movement.</summary>
    /// <returns>The sample.</returns>
    LocalInputSample Take();
}
