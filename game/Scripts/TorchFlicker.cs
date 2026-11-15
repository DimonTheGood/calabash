using UnityEngine;

namespace Calabash.Game
{
    /// <summary>Дрожащий свет факела или костра: яркость и дальность гуляют по шуму Перлина.</summary>
    [RequireComponent(typeof(Light))]
    public class TorchFlicker : MonoBehaviour
    {
        public float intensity = 6f;
        [Range(0f, 0.6f)] public float amount = 0.25f;
        public float speed = 9f;
        Light lamp;
        float seed, range;

        void Awake()
        {
            lamp = GetComponent<Light>();
            seed = Random.value * 50f;
            range = lamp.range;
        }

        void Update()
        {
            float n = Mathf.PerlinNoise(Time.time * speed, seed) * 0.7f + Mathf.PerlinNoise(Time.time * speed * 2.7f, seed + 9f) * 0.3f;
            float k = 1f + amount * (n - 0.5f) * 2f;
            lamp.intensity = intensity * k;
            lamp.range = range * (0.96f + 0.04f * k);
        }
    }
}
