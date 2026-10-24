namespace KingOfNothing.Core
{
    /// <summary>
    /// Roadmap phase 1: the game starts either in the normal mode or in developer mode.
    /// Developer mode unlocks debug tools (mouse interaction with objects, spawning items / characters,
    /// editing traits). The final default mode will be built from a subset of these tools.
    /// </summary>
    public enum GameMode
    {
        Default,
        Developer,
    }
}
