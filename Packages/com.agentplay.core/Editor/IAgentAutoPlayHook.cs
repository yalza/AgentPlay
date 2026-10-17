namespace AgentPlay.Editor
{
    /// <summary>
    /// A game's setup and teardown around an AutoPlay run, e.g. backing up and restoring the player's save.
    /// Created with its parameterless constructor; keep state on disk, not in fields (a domain reload sits between
    /// the two calls). AfterPlay runs after Play mode has ended, also after a deadline or a failed hook.
    /// </summary>
    public interface IAgentAutoPlayHook
    {
        void BeforePlay();
        void AfterPlay();
    }
}
