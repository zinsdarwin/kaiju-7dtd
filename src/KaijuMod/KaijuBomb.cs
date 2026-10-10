using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The Oxygen Destroyer dropped from a gyrocopter: it falls from the gyro's position with its
    /// speed, fins steering it onto him. Into him (within HitRadius of his centre, below the top of
    /// his head): KaijuRun.OnBombHit. Onto the ground or the sea, or he has gone: OnBombMissed.
    /// </summary>
    public class KaijuBomb : WorldAnchored
    {
        /// <summary>It hits him within this distance of his centre, metres.</summary>
        public static float HitRadius = 35f;
        /// <summary>How hard its fins steer it toward him, m/s².</summary>
        public static float Steer = 14f;
        private const float Gravity = 9.81f;
        private const string ModelPath = "@:Entities/Industrial/tankPropanePrefab.prefab";

        public static KaijuBomb Falling { get; private set; }

        private Vector3 vel;
        private Transform model;

        public static KaijuBomb Drop(Vector3 worldPos, Vector3 velocity)
        {
            var go = new GameObject("KaijuOxygenDestroyerBomb");
            Object.DontDestroyOnLoad(go);
            var b = go.AddComponent<KaijuBomb>();
            b.worldPos = worldPos;
            b.vel = velocity;
            b.LateUpdate();
            b.model = MakeModel(go.transform);
            var glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(0.5f, 0.85f, 1f);
            glow.range = 20f;
            glow.intensity = 3f;
            glow.shadows = LightShadows.None;
            Falling = b;
            return b;
        }

        /// <summary>The device's own model (the block's propane tank), else a capsule.</summary>
        private static Transform MakeModel(Transform parent)
        {
            GameObject m = null;
            try
            {
                // VERIFIED (V3.3): DataLoader.LoadAsset<T>("@:...") loads addressable game assets
                // (GameApi.LoadAudioClip). UNVERIFIED in game: a block's model prefab instantiates
                // cleanly on its own.
                var prefab = DataLoader.LoadAsset<GameObject>(ModelPath);
                if (prefab != null)
                    m = Object.Instantiate(prefab);
            }
            catch (System.Exception e)
            {
                Log.Warning("[KaijuMod] Bomb model: " + e.Message);
            }
            if (m == null)
            {
                m = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                m.transform.localScale = new Vector3(1.2f, 2.5f, 1.2f);
            }
            foreach (var c in m.GetComponentsInChildren<Collider>())
                Object.Destroy(c);
            foreach (var mb in m.GetComponentsInChildren<MonoBehaviour>())
                mb.enabled = false;
            m.transform.SetParent(parent, false);
            m.transform.localPosition = Vector3.zero;
            return m.transform;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            var d = KaijuDirector.Instance;
            var run = KaijuRun.Instance;
            World world = GameApi.World;
            if (world == null || !run.Active)
            {
                Finish();
                return;
            }
            vel.y -= Gravity * dt;
            if (d.Running && !d.Dying)
            {
                // Fins: steer the horizontal speed toward him.
                Vector2 to = d.Position - new Vector2(worldPos.x, worldPos.z);
                Vector2 want = to.normalized * Mathf.Min(40f, to.magnitude);
                Vector2 h = Vector2.MoveTowards(new Vector2(vel.x, vel.z), want, Steer * dt);
                vel.x = h.x;
                vel.z = h.y;
            }
            worldPos += vel * dt;
            if (model != null)
                model.rotation = Quaternion.FromToRotation(Vector3.forward, vel.sqrMagnitude > 0.01f ? vel.normalized : Vector3.down);

            if (d.Running && !d.Dying)
            {
                Vector2 off = d.Position - new Vector2(worldPos.x, worldPos.z);
                if (off.sqrMagnitude <= HitRadius * HitRadius && worldPos.y <= d.BaseY + d.Footprint.Height)
                {
                    run.OnBombHit(worldPos);
                    Finish();
                    return;
                }
            }
            float ground = Mathf.Max(GameApi.GroundHeight(world, Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.z)),
                run.Data != null ? run.Data.SeaLevel : 0f);
            if (worldPos.y <= ground)
            {
                run.OnBombMissed(worldPos);
                Finish();
            }
        }

        private void Finish()
        {
            if (Falling == this)
                Falling = null;
            Destroy(gameObject);
        }
    }
}
