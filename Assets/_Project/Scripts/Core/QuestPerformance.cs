using UnityEngine;

namespace ArcaneStrike.Core
{
    /// <summary>
    /// On the headset, keeps the frame rate steady: it locks the display to 72 Hz and asks for sustained-high CPU and
    /// GPU clocks. It also turns on dynamic foveated rendering, which draws the edges of the lenses at lower resolution
    /// where it isn't seen. The game targets 72 fps, so it never drops under 60. 72 Hz is the lowest rate a Quest 3 can
    /// show (a Quest 2 can also do 60), and 72 fps is Meta's minimum for Quest apps.
    /// Created by itself at start-up; does nothing in the editor or on desktop.
    /// </summary>
    public static class QuestPerformance
    {
        public const float TargetHz = 72f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // The rate nearest 72 Hz that the headset offers (and never under 60).
                float best = 0f;
                float[] rates = OVRPlugin.systemDisplayFrequenciesAvailable;
                if (rates != null)
                    foreach (float r in rates)
                        if (r >= 60f && (best == 0f || Mathf.Abs(r - TargetHz) < Mathf.Abs(best - TargetHz))) best = r;
                if (best > 0f && !Mathf.Approximately(OVRPlugin.systemDisplayFrequency, best)) OVRPlugin.systemDisplayFrequency = best;

                OVRPlugin.suggestedCpuPerfLevel = OVRPlugin.ProcessorPerformanceLevel.SustainedHigh;
                OVRPlugin.suggestedGpuPerfLevel = OVRPlugin.ProcessorPerformanceLevel.SustainedHigh;

                OVRPlugin.foveatedRenderingLevel = OVRPlugin.FoveatedRenderingLevel.High;
                OVRPlugin.useDynamicFoveatedRendering = true;

                // Physics (the sword, the hands) steps once per displayed frame.
                if (best > 0f) Time.fixedDeltaTime = 1f / best;
                Debug.Log($"[Quest] Display {OVRPlugin.systemDisplayFrequency} Hz (offered: {(rates == null ? "?" : string.Join(", ", rates))}), " +
                          "CPU/GPU sustained high, dynamic foveation high.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Quest] Couldn't set the performance options: {e.Message}");
            }
#endif
        }
    }
}
