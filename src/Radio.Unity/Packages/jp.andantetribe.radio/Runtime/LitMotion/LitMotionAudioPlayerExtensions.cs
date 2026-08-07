#if ENABLE_LITMOTION
#nullable enable

using System;

namespace Radio
{
    /// <summary>
    /// Enables LitMotion-based BGM transitions.
    /// </summary>
    public static class LitMotionAudioPlayerExtensions
    {
        /// <summary>
        /// Configures a stateful LitMotion cross-fade transition for this player.
        /// </summary>
        public static TPlayer UseLitMotionCrossFade<TPlayer>(
            this TPlayer player,
            TimeSpan fadeDuration)
            where TPlayer : AudioPlayer
        {
            if (player.Sources.Bgm.Count < 2)
            {
                throw new InvalidOperationException(
                    "LitMotion cross-fades require at least two BGM channels.");
            }

            player.ConfigureBgmTransition(new LitMotionCrossFadeTransition(fadeDuration));
            return player;
        }
    }
}
#endif
