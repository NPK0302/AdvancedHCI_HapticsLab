using TMPro;
using UnityEngine;

namespace HapticsLab
{
    /// <summary>
    /// A tiny perception experiment. Two identical-looking panels, A and B, get the same
    /// material and the same sound, but two different (hidden) fidelity levels
    /// (pulse, designed clip or clip + dynamics; never "Off").
    /// The participant touches both and votes which felt more like the real material.
    /// The score shows how often the higher-fidelity panel was picked: a mini A/B study
    /// that groups can discuss (and repeat with more participants).
    /// </summary>
    [AddComponentMenu("Haptics Lab/Blind Test")]
    public class BlindTest : MonoBehaviour
    {
        public HapticSurface panelA;
        public HapticSurface panelB;
        [Tooltip("Surfaces to borrow materials from (the Material Wall panels).")]
        public HapticSurface[] materials;

        public TMP_Text prompt;
        public TMP_Text result;
        public TMP_Text score;

        Fidelity _a, _b;
        bool _touchedA, _touchedB, _voted;
        int _trials, _richerPreferred;
        // How often each level was presented and chosen.
        readonly int[] _shown = new int[4];
        readonly int[] _chosen = new int[4];

        void OnEnable()
        {
            if (panelA) panelA.Touched += OnTouched;
            if (panelB) panelB.Touched += OnTouched;
        }

        void OnDisable()
        {
            if (panelA) panelA.Touched -= OnTouched;
            if (panelB) panelB.Touched -= OnTouched;
        }

        void Start() => NewTrial();

        void OnTouched(HapticSurface s)
        {
            if (s == panelA) _touchedA = true;
            if (s == panelB) _touchedB = true;
        }

        public void NewTrial()
        {
            if (materials == null || materials.Length == 0 || panelA == null || panelB == null) return;
            var m = materials[Random.Range(0, materials.Length)];
            panelA.CopyMaterialFrom(m);
            panelB.CopyMaterialFrom(m);

            // "Off" is left out: against no haptics at all the choice is too easy to tell us anything.
            _a = (Fidelity)Random.Range(1, 4);
            do { _b = (Fidelity)Random.Range(1, 4); } while (_b == _a);
            panelA.overrideFidelity = panelB.overrideFidelity = true;
            panelA.fidelity = _a;
            panelB.fidelity = _b;

            _touchedA = _touchedB = _voted = false;
            if (prompt) prompt.text = $"Material: <b>{m.materialName}</b>\nTap and rub <b>A</b> and <b>B</b>.\nWhich feels more like real {m.materialName.ToLowerInvariant()}?";
            if (result) result.text = "";
            UpdateScore();
        }

        public void VoteA() => Vote(true);
        public void VoteB() => Vote(false);

        void Vote(bool pickedA)
        {
            if (_voted) { if (result) result.text = "Already voted - press <b>Next round</b>."; return; }
            if (!_touchedA || !_touchedB) { if (result) result.text = "Touch <b>both</b> panels first!"; return; }

            _voted = true;
            var chosen = pickedA ? _a : _b;
            var other = pickedA ? _b : _a;
            bool richer = chosen > other;
            _trials++;
            if (richer) _richerPreferred++;
            _shown[(int)_a]++;
            _shown[(int)_b]++;
            _chosen[(int)chosen]++;

            if (result)
                result.text = $"A was <b>{FidelityInfo.Label(_a)}</b>\nB was <b>{FidelityInfo.Label(_b)}</b>\n" +
                              (richer ? "You picked the <color=#8f8>higher-fidelity</color> one."
                                      : "You picked the <color=#fb6>lower-fidelity</color> one!");
            UpdateScore();
        }

        public void ResetScore()
        {
            _trials = _richerPreferred = 0;
            System.Array.Clear(_shown, 0, 4);
            System.Array.Clear(_chosen, 0, 4);
            NewTrial();
        }

        void UpdateScore()
        {
            if (!score) return;
            int pct = _trials > 0 ? Mathf.RoundToInt(100f * _richerPreferred / _trials) : 0;
            var s = $"Rounds: <b>{_trials}</b>   Higher fidelity picked: <b>{_richerPreferred}</b> ({pct}%)\n";
            for (int i = 1; i < 4; i++)
                s += $"{FidelityInfo.Label((Fidelity)i)}: picked {_chosen[i]} of {_shown[i]}   ";
            score.text = s;
        }
    }
}
