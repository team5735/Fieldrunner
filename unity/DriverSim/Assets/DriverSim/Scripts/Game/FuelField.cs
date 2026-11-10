using System;
using System.Collections.Generic;
using DriverSim.Field;
using DriverSim.Net;
using UnityEngine;

namespace DriverSim.Game
{
    /// <summary>
    /// Owns every fuel ball: the pool, the balls resting on the field (Rigidbody spheres, the game's only physics), the
    /// balls flying a scripted arc after a launch, and the balls waiting to return from a hub after a score.
    ///
    /// Balls are spawned at the field model's staged positions. A launched ball follows <see cref="ShotModel"/>'s arc
    /// and ends by scoring (it is hidden and reappears at the hub's exit after <see cref="ReturnDelayS"/>), by hitting
    /// a hub's body (it becomes a physics ball with its velocity reflected) or by landing (a physics ball with its
    /// current velocity). Also takes <c>spawnFuel</c> commands from the bridge's command topic.
    /// </summary>
    public sealed class FuelField : MonoBehaviour
    {
        public const float ReturnDelayS = 1.5f; // seconds a scored ball waits before it leaves the hub again

        private const float FoamSmoothness = 0.2f; // URP Lit smoothness of the ball material
        private const float BallDiameterM = 0.15f; // meters, the fuel ball
        private const float BallMassKg = 0.215f; // kilograms
        private const float LinearDamping = 1.5f; // per second; settles a rolling ball quickly
        private const float AngularDamping = 1f; // per second
        private const float MaxSpeedMps = 8f; // fastest a physics ball may move
        private const float MaxDepenetrationMps = 3f; // fastest overlapping bodies push a ball apart
        private const float SleepThreshold = 0.05f; // kinetic energy below which a ball sleeps
        private const float Friction = 0.6f; // ball against carpet, walls and other balls
        private const float Bounciness = 0.35f; // fraction of speed kept on a bounce
        private const float MaxFlightStepS = 0.01f; // longest time one flight step covers
        private const float ReturnExitOffsetM = 0.8f; // meters from the hub center to where a scored ball reappears
        private const float ReturnHeightM = 0.45f; // meters above the carpet where it reappears
        private const float ReturnSpeedMps = 2.4f; // meters/s it rolls out at
        private const float ReturnSpeedSpreadMps = 0.5f; // meters/s, random variation of that speed
        private const float ReturnSpreadDeg = 25f; // degrees, random spread either side of straight at the center
        private const float EdgeMarginM = 0.1f; // meters; fuel outside the field by more than this is put back
        private const float RecoveredHeightM = 0.2f; // meters above the carpet where a lost ball is put back
        private const float LostBelowM = -0.3f; // meters; a ball below this fell through the floor

        [Tooltip("Link to the bridge, for spawnFuel commands.")]
        [SerializeField] private DriverSimLink link;

        [Tooltip("Field model preparation, where the staged fuel positions come from.")]
        [SerializeField] private FieldSetup fieldSetup;

        [Tooltip("The Fuel.glb model drawn for each ball.")]
        [SerializeField] private GameObject fuelModel;

        private readonly List<FuelBall> balls = new List<FuelBall>(); // every ball ever made
        private readonly Stack<FuelBall> pool = new Stack<FuelBall>(); // hidden balls ready for reuse
        private readonly List<FuelBall> flying = new List<FuelBall>(); // balls on a scripted arc
        private readonly List<FuelBall> returning = new List<FuelBall>(); // scored balls waiting to return
        private readonly List<FuelBall> resting = new List<FuelBall>(); // physics balls on the field
        private HubFootprint[] hubs; // both hubs
        private readonly List<ObstacleSpec> blockers = new List<ObstacleSpec>(); // solid obstacles a landing avoids
        private GameObject visualTemplate; // inactive ball visual copied into every ball
        private PhysicsMaterial ballMaterial; // friction and bounce of every ball
        private int restingCount; // balls in the Resting state
        private int flyingCount; // balls in the Flying state

        /// <summary>Raised when a flying ball scores; the argument is true for the red hub.</summary>
        public event Action<bool> FuelScored;

        /// <summary>Sets the references the field needs; call before the scene starts.</summary>
        /// <param name="driverSimLink">Link to the bridge.</param>
        /// <param name="setup">Field model preparation.</param>
        /// <param name="ballModel">The Fuel.glb model.</param>
        public void SetReferences(DriverSimLink driverSimLink, FieldSetup setup, GameObject ballModel)
        {
            link = driverSimLink;
            fieldSetup = setup;
            fuelModel = ballModel;
        }

        /// <summary>Fuel balls resting on the field.</summary>
        public int GetFieldCount()
        {
            return restingCount;
        }

        /// <summary>Fuel balls in the air.</summary>
        public int GetFlyingCount()
        {
            return flyingCount;
        }

        /// <summary>Scored fuel waiting to return from a hub.</summary>
        public int GetReturningCount()
        {
            return returning.Count;
        }

        /// <summary>The hubs the balls score in; index 0 is blue, 1 red.</summary>
        public HubFootprint[] GetHubs()
        {
            return hubs;
        }

        /// <summary>The balls resting on the field now; the list is reused, so do not keep it.</summary>
        public IReadOnlyList<FuelBall> GetRestingBalls()
        {
            return resting;
        }

        /// <summary>Removes every ball from play, then puts one at each staged position.</summary>
        public void RespawnAll()
        {
            ClearAll();
            IReadOnlyList<Vector3> staged = fieldSetup.GetStagedFuelPositions();
            foreach (Vector3 position in staged)
            {
                SpawnResting(position);
            }
        }

        /// <summary>Removes every ball from play.</summary>
        public void ClearAll()
        {
            pool.Clear();
            foreach (FuelBall ball in balls)
            {
                if (ball.GetState() != FuelState.Hidden)
                {
                    ChangeState(ball, FuelState.Hidden);
                }
                ball.ClearScored();
                pool.Push(ball);
            }
            flying.Clear();
            returning.Clear();
            resting.Clear();
        }

        /// <summary>Puts a ball on the field, at rest.</summary>
        /// <param name="positionWorld">Ball center, world space, meters.</param>
        /// <returns>The ball.</returns>
        public FuelBall SpawnResting(Vector3 positionWorld)
        {
            FuelBall ball = AcquireBall();
            PlaceResting(ball, positionWorld, Vector3.zero);
            return ball;
        }

        /// <summary>Starts a ball on a scripted arc.</summary>
        /// <param name="startWorld">Launch point, world space, meters.</param>
        /// <param name="velocityWorldMps">Launch velocity, world space, meters/s.</param>
        /// <returns>The ball.</returns>
        public FuelBall Launch(Vector3 startWorld, Vector3 velocityWorldMps)
        {
            FuelBall ball = AcquireBall();
            FlightPlan plan = ShotModel.MakeFlightPlan(startWorld, velocityWorldMps, hubs);
            ball.StartFlight(plan);
            ChangeState(ball, FuelState.Flying);
            ball.transform.position = startWorld;
            flying.Add(ball);
            return ball;
        }

        /// <summary>Takes a resting ball off the field (into the hopper).</summary>
        /// <param name="ball">Ball to remove.</param>
        public void Remove(FuelBall ball)
        {
            resting.Remove(ball);
            ChangeState(ball, FuelState.Hidden);
            pool.Push(ball);
        }

        // ---- Unity messages

        private void Awake()
        {
            GameLayers.ApplyCollisionMatrix();
            hubs = ShotModel.MakeHubs();
            CollectBlockers();
            MakeBallMaterial();
            MakeVisualTemplate();
        }

        private void Start()
        {
            RespawnAll();
            Debug.Log("[Driver-Sim] spawned " + restingCount + " fuel balls");
        }

        private void Update()
        {
            TakeSpawnRequests();
            StepFlights(Time.deltaTime);
            StepReturns();
        }

        private void FixedUpdate()
        {
            KeepRestingBallsInPlay();
        }

        // ---- private stateful helpers

        /// <summary>Takes the solid obstacle rectangles a landing ball must not end up inside.</summary>
        private void CollectBlockers()
        {
            List<ObstacleSpec> specs = FieldObstacleLayout.GetAll();
            foreach (ObstacleSpec spec in specs)
            {
                if (spec.Kind == ObstacleKind.Solid)
                {
                    blockers.Add(spec);
                }
            }
        }

        /// <summary>Makes the friction and bounce material shared by every ball.</summary>
        private void MakeBallMaterial()
        {
            ballMaterial = new PhysicsMaterial("Fuel");
            ballMaterial.dynamicFriction = Friction;
            ballMaterial.staticFriction = Friction;
            ballMaterial.bounciness = Bounciness;
            ballMaterial.frictionCombine = PhysicsMaterialCombine.Average;
            ballMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
        }

        /// <summary>
        /// Builds the ball visual once: the Fuel.glb model scaled to the ball's diameter and centered, with any
        /// colliders and shadows removed. Without a model it is a plain yellow sphere.
        /// </summary>
        private void MakeVisualTemplate()
        {
            visualTemplate = new GameObject("FuelVisualTemplate");
            visualTemplate.transform.SetParent(transform, false);
            if (fuelModel == null)
            {
                Debug.LogWarning("[Driver-Sim] FuelField has no fuel model; drawing plain spheres");
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(visualTemplate.transform, false);
                sphere.transform.localScale = Vector3.one * BallDiameterM;
                visualTemplate.SetActive(false);
                return;
            }

            // measure the model, then scale and center it
            GameObject model = Instantiate(fuelModel, visualTemplate.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            Bounds bounds = MeasureBounds(model);
            float largestM = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = 1;
            if (largestM > 0)
            {
                scale = BallDiameterM / largestM;
            }
            model.transform.localScale = Vector3.one * scale;
            Bounds scaled = MeasureBounds(model);
            model.transform.localPosition = -scaled.center;

            // visual only
            Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                Destroy(collider);
            }
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer ballRenderer in renderers)
            {
                ballRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                // foam look: not metallic, low gloss (URP Lit and the glTF shader name these differently); the
                // template's material copies are shared by every ball
                Material[] materials = ballRenderer.materials;
                foreach (Material visualMaterial in materials)
                {
                    SetIfPresent(visualMaterial, "_Metallic", 0f);
                    SetIfPresent(visualMaterial, "metallicFactor", 0f);
                    SetIfPresent(visualMaterial, "_Smoothness", FoamSmoothness);
                    SetIfPresent(visualMaterial, "roughnessFactor", 1f - FoamSmoothness);
                }
            }
            visualTemplate.SetActive(false);
        }

        /// <summary>Takes a hidden ball from the pool, or makes one.</summary>
        private FuelBall AcquireBall()
        {
            if (pool.Count > 0)
            {
                return pool.Pop();
            }
            FuelBall ball = MakeBall();
            balls.Add(ball);
            return ball;
        }

        /// <summary>Makes one ball: a rigidbody sphere with the fuel visual, hidden.</summary>
        private FuelBall MakeBall()
        {
            GameObject ballObject = new GameObject("Fuel");
            ballObject.layer = GameLayers.Fuel;
            ballObject.transform.SetParent(transform, false);

            // physics
            SphereCollider ballCollider = ballObject.AddComponent<SphereCollider>();
            ballCollider.radius = BallDiameterM / 2;
            ballCollider.sharedMaterial = ballMaterial;
            Rigidbody body = ballObject.AddComponent<Rigidbody>();
            body.mass = BallMassKg;
            body.linearDamping = LinearDamping;
            body.angularDamping = AngularDamping;
            body.maxDepenetrationVelocity = MaxDepenetrationMps;
            body.sleepThreshold = SleepThreshold;

            // visual
            GameObject visual = Instantiate(visualTemplate, ballObject.transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero;
            visual.SetActive(true);

            FuelBall ball = ballObject.AddComponent<FuelBall>();
            ball.SetParts(body, ballCollider);
            ballObject.SetActive(false);
            return ball;
        }

        /// <summary>Switches a ball between resting, flying and hidden, setting its parts to match.</summary>
        /// <param name="ball">Ball to change.</param>
        /// <param name="newState">State to enter.</param>
        private void ChangeState(FuelBall ball, FuelState newState)
        {
            FuelState oldState = ball.GetState();
            if (oldState == FuelState.Resting)
            {
                restingCount--;
            }
            else if (oldState == FuelState.Flying)
            {
                flyingCount--;
            }
            Rigidbody body = ball.GetBody();
            SphereCollider ballCollider = ball.GetCollider();
            if (newState == FuelState.Hidden)
            {
                ball.gameObject.SetActive(false);
            }
            else
            {
                ball.gameObject.SetActive(true);
                bool isResting = newState == FuelState.Resting;
                ballCollider.enabled = isResting;
                body.isKinematic = !isResting;
            }
            if (newState == FuelState.Resting)
            {
                restingCount++;
            }
            else if (newState == FuelState.Flying)
            {
                flyingCount++;
            }
            ball.SetState(newState);
        }

        /// <summary>Makes a ball a physics ball at a place, moving at a velocity.</summary>
        /// <param name="ball">Ball to place.</param>
        /// <param name="positionWorld">Ball center, world space, meters.</param>
        /// <param name="velocityWorldMps">Velocity, world space, meters/s.</param>
        private void PlaceResting(FuelBall ball, Vector3 positionWorld, Vector3 velocityWorldMps)
        {
            bool wasResting = ball.GetState() == FuelState.Resting;
            ChangeState(ball, FuelState.Resting);
            if (!wasResting)
            {
                resting.Add(ball);
            }
            ball.transform.position = positionWorld;
            Rigidbody body = ball.GetBody();
            body.position = positionWorld;
            body.linearVelocity = velocityWorldMps;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }

        /// <summary>Turns each waiting spawnFuel command into a ball: on the carpet, or released at a height.</summary>
        private void TakeSpawnRequests()
        {
            if (link == null)
            {
                return;
            }
            while (link.TryTakeSpawnRequest(out FuelSpawnRequest request))
            {
                if (request.HeightM <= 0)
                {
                    Vector3 onCarpet = FieldFrame.ConvertFieldToWorld(request.XFieldM, request.YFieldM,
                        ShotModel.BallRadiusM);
                    SpawnResting(onCarpet);
                }
                else
                {
                    Vector3 released = FieldFrame.ConvertFieldToWorld(request.XFieldM, request.YFieldM,
                        request.HeightM);
                    Launch(released, Vector3.zero);
                }
            }
        }

        /// <summary>Moves every flying ball along its arc for one frame and acts on how its flight ends.</summary>
        /// <param name="dtS">Seconds since the last frame.</param>
        private void StepFlights(float dtS)
        {
            for (int i = flying.Count - 1; i >= 0; i--)
            {
                FuelBall ball = flying[i];
                float remainingS = dtS;
                while (remainingS > 0 && ball.GetState() == FuelState.Flying)
                {
                    float stepS = Mathf.Min(remainingS, MaxFlightStepS);
                    float fromS = ball.GetFlightS();
                    float toS = fromS + stepS;
                    FlightStep step = ShotModel.StepFlight(ball.GetFlightPlan(), fromS, toS, hubs);
                    ball.SetFlightS(toS);
                    remainingS -= stepS;
                    ball.transform.position = step.PositionWorld;
                    if (step.Result == FlightResult.Scored)
                    {
                        EndFlightScored(ball, step);
                    }
                    else if (step.Result == FlightResult.HitHub)
                    {
                        EndFlightHitHub(ball, step);
                    }
                    else if (step.Result == FlightResult.Landed)
                    {
                        EndFlightLanded(ball, step);
                    }
                }
                if (ball.GetState() != FuelState.Flying)
                {
                    flying.RemoveAt(i);
                }
            }
        }

        /// <summary>A ball dropped into a hub: it leaves play until it returns, and the score is announced.</summary>
        private void EndFlightScored(FuelBall ball, FlightStep step)
        {
            ChangeState(ball, FuelState.Hidden);
            ball.SetScored(step.HubIndex, Time.time + ReturnDelayS);
            returning.Add(ball);
            bool isRed = hubs[step.HubIndex].IsRed;
            if (FuelScored != null)
            {
                FuelScored(isRed);
            }
        }

        /// <summary>A ball that entered a hub's body: a physics ball outside it, moving off the face.</summary>
        private void EndFlightHitHub(FuelBall ball, FlightStep step)
        {
            HubFootprint hub = hubs[step.HubIndex];
            BodyBounce bounce = ShotModel.ComputeHubBounce(hub, step.PositionWorld, step.VelocityWorldMps);
            PlaceResting(ball, bounce.PositionWorld, bounce.VelocityWorldMps);
        }

        /// <summary>A ball that came down on the field: a physics ball, kept out of solid obstacles.</summary>
        private void EndFlightLanded(FuelBall ball, FlightStep step)
        {
            Vector3 position = step.PositionWorld;
            foreach (ObstacleSpec blocker in blockers)
            {
                Vector3 minCorner = FieldFrame.ConvertFieldToWorld(blocker.MinXFieldM, blocker.MinYFieldM, 0);
                Vector3 maxCorner = FieldFrame.ConvertFieldToWorld(blocker.MaxXFieldM, blocker.MaxYFieldM, 0);
                Vector3 normal;
                position = ShotModel.PushOutOfRectangle(position, minCorner.x, maxCorner.x, minCorner.z, maxCorner.z,
                    out normal);
            }
            PlaceResting(ball, position, step.VelocityWorldMps);
        }

        /// <summary>Brings scored balls back out of their hub once their wait is over.</summary>
        private void StepReturns()
        {
            for (int i = returning.Count - 1; i >= 0; i--)
            {
                FuelBall ball = returning[i];
                if (Time.time < ball.GetReturnTimeS())
                {
                    continue;
                }
                HubFootprint hub = hubs[ball.GetScoredHubIndex()];
                ReturnFromHub(ball, hub);
                ball.ClearScored();
                returning.RemoveAt(i);
            }
        }

        /// <summary>Puts a scored ball at the hub's exit, facing the field center, rolling out with some spread.</summary>
        private void ReturnFromHub(FuelBall ball, HubFootprint hub)
        {
            Vector3 center = hub.OpeningCenterWorld;
            Vector3 towardFieldCenter = new Vector3(-center.x, 0, -center.z).normalized;
            float spreadDeg = UnityEngine.Random.Range(-ReturnSpreadDeg, ReturnSpreadDeg);
            Quaternion spread = Quaternion.AngleAxis(spreadDeg, Vector3.up);
            Vector3 direction = spread * towardFieldCenter;
            float speedMps = ReturnSpeedMps + UnityEngine.Random.Range(-ReturnSpeedSpreadMps, ReturnSpeedSpreadMps);
            Vector3 position = center + towardFieldCenter * ReturnExitOffsetM;
            position.y = ReturnHeightM;
            PlaceResting(ball, position, direction * speedMps);
        }

        /// <summary>Caps the balls' speed and puts back any that left the field.</summary>
        private void KeepRestingBallsInPlay()
        {
            float halfLengthM = FieldFrame.FieldLengthM / 2;
            float halfWidthM = FieldFrame.FieldWidthM / 2;
            foreach (FuelBall ball in resting)
            {
                Rigidbody body = ball.GetBody();
                if (body.IsSleeping())
                {
                    continue;
                }
                Vector3 velocity = body.linearVelocity;
                float speedMps = velocity.magnitude;
                if (speedMps > MaxSpeedMps)
                {
                    body.linearVelocity = velocity * (MaxSpeedMps / speedMps);
                }

                // outside the field, or fallen through the floor
                Vector3 position = body.position;
                bool isOutX = Mathf.Abs(position.x) > halfLengthM + EdgeMarginM;
                bool isOutZ = Mathf.Abs(position.z) > halfWidthM + EdgeMarginM;
                bool isLost = position.y < LostBelowM;
                if (isOutX || isOutZ || isLost)
                {
                    float x = Mathf.Clamp(position.x, -halfLengthM + EdgeMarginM, halfLengthM - EdgeMarginM);
                    float z = Mathf.Clamp(position.z, -halfWidthM + EdgeMarginM, halfWidthM - EdgeMarginM);
                    Vector3 recovered = new Vector3(x, RecoveredHeightM, z);
                    body.position = recovered;
                    ball.transform.position = recovered;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Sets a float property when the material's shader has it.</summary>
        private static void SetIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        /// <summary>Bounds of every renderer under a model, world space.</summary>
        private static Bounds MeasureBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(model.transform.position, Vector3.zero);
            }
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }
    }
}
