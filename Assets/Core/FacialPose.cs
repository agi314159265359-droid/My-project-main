using System.Collections.Generic;
using UnityEngine;


namespace Mikk.Avatar
{
    [System.Serializable]
    public class FacialPose
    {
        public Dictionary<string, float> weights = new Dictionary<string, float>();

        public FacialPose() { }

        public FacialPose(params (string name, float value)[] blendshapes)
        {
            foreach (var (name, value) in blendshapes)
            {
                if (value > 0.001f)
                    weights[name] = value;
            }
        }

        public static FacialPose Lerp(FacialPose a, FacialPose b, float t)
        {
            var result = new FacialPose();
            var allKeys = new HashSet<string>();

            if (a?.weights != null)
                foreach (var key in a.weights.Keys) allKeys.Add(key);
            if (b?.weights != null)
                foreach (var key in b.weights.Keys) allKeys.Add(key);

            foreach (var key in allKeys)
            {
                float va = (a?.weights != null && a.weights.TryGetValue(key, out var av)) ? av : 0f;
                float vb = (b?.weights != null && b.weights.TryGetValue(key, out var bv)) ? bv : 0f;
                float blended = Mathf.Lerp(va, vb, t);

                if (blended > 0.001f)
                    result.weights[key] = blended;
            }

            return result;
        }

        public FacialPose WithVariation(float amount)
        {
            var result = new FacialPose();
            foreach (var kvp in weights)
            {
                float varied = kvp.Value + Random.Range(-amount, amount);
                result.weights[kvp.Key] = Mathf.Clamp01(varied);
            }
            return result;
        }

        public int ActiveCount => weights.Count;
        public static FacialPose Neutral => new FacialPose();
    }
}