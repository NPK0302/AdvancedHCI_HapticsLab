using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace HapticsLab
{
    /// <summary>Which controller(s) a haptic effect should play on.</summary>
    public enum Hand
    {
        Left,
        Right,
        Both
    }

    public static class HandUtil
    {
        /// <summary>
        /// Works out which hand an XRI interactor belongs to. XRI 3 exposes
        /// <c>handedness</c> directly; the name check is only a fallback for
        /// custom rigs where handedness is left at "None".
        /// </summary>
        public static Hand FromInteractor(IXRInteractor interactor, Hand fallback = Hand.Right)
        {
            if (interactor == null)
                return fallback;

            switch (interactor.handedness)
            {
                case InteractorHandedness.Left:
                    return Hand.Left;
                case InteractorHandedness.Right:
                    return Hand.Right;
            }

            for (var t = interactor.transform; t != null; t = t.parent)
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("left"))
                    return Hand.Left;
                if (n.Contains("right"))
                    return Hand.Right;
            }

            return fallback;
        }

        /// <summary>Convenience overload for any XRI event (hover, select, activate...).</summary>
        public static Hand FromArgs(BaseInteractionEventArgs args, Hand fallback = Hand.Right)
        {
            return args == null ? fallback : FromInteractor(args.interactorObject, fallback);
        }

        internal static Oculus.Haptics.Controller ToMeta(Hand hand)
        {
            switch (hand)
            {
                case Hand.Left:
                    return Oculus.Haptics.Controller.Left;
                case Hand.Right:
                    return Oculus.Haptics.Controller.Right;
                default:
                    return Oculus.Haptics.Controller.Both;
            }
        }
    }
}
