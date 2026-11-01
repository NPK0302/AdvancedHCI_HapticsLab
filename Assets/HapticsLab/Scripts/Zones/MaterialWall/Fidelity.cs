namespace HapticsLab
{
    /// <summary>
    /// Four levels of haptic fidelity, from nothing to fully designed and dynamic.
    /// The audio is identical at every level, so only touch changes.
    /// </summary>
    public enum Fidelity
    {
        /// <summary>No haptics at all - audio and visuals only.</summary>
        Off = 0,
        /// <summary>XRI SendHapticImpulse: one amplitude, one duration. A generic "buzz".</summary>
        SimplePulse = 1,
        /// <summary>A .haptic clip designed in Haptics Studio, played as-is.</summary>
        DesignedClip = 2,
        /// <summary>Designed clips driven by the interaction: taps scale with impact speed,
        /// textures play one grain per ridge the finger crosses.</summary>
        DesignedDynamic = 3,
    }

    public static class FidelityInfo
    {
        public static string Label(Fidelity f)
        {
            switch (f)
            {
                case Fidelity.Off: return "0  Off";
                case Fidelity.SimplePulse: return "1  Simple pulse";
                case Fidelity.DesignedClip: return "2  Designed clip";
                default: return "3  Clip + dynamics";
            }
        }

        public static string Description(Fidelity f)
        {
            switch (f)
            {
                case Fidelity.Off:
                    return "<b>Off</b> - no haptics. Only sound and visuals.\nHow much does touch add?";
                case Fidelity.SimplePulse:
                    return "<b>Simple pulse</b> - SendHapticImpulse(0.5, 0.1 s), XRI's default.\nEvery material feels the same: a generic buzz.";
                case Fidelity.DesignedClip:
                    return "<b>Designed clip</b> - a .haptic clip per material (Haptics Studio),\nplayed as designed. Materials now differ.";
                default:
                    return "<b>Clip + dynamics</b> - taps follow how hard you hit; rubbing plays\none grain per ridge your finger crosses. Go slow, go fast, stop.";
            }
        }
    }
}
