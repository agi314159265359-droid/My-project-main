
using FishNet;
using FishNet.Component.Prediction;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using GameKit.Dependencies.Utilities;
using UnityEngine;


namespace FirstGearGames.LobbyAndWorld.Demos.KingOfTheHill
{

    public class MotorRB : NetworkBehaviour
    {
        #region Types.
        public struct MoveData : IReplicateData
        {
            public float Horizontal;
            public float Vertical;
            public MoveData(float horizontal, float vertical) : this()
            {
                Horizontal = horizontal;
                Vertical = vertical;
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;

        }
        public struct ReconcileData : IReconcileData
        {
            public RigidbodyState RigidbodyState;
            public PredictionRigidbody PredictionRigidbody;
            public ReconcileData(PredictionRigidbody pr) : this()
            {
                PredictionRigidbody = pr;
                RigidbodyState = new RigidbodyState(pr.Rigidbody);
            }

            private uint _tick;
            public void Dispose() { }
            public uint GetTick() => _tick;
            public void SetTick(uint value) => _tick = value;
        }
        #endregion

        /// <summary>
        /// How quickly to accelerate.
        /// </summary>
        [Tooltip("How quickly to accelerate.")]
        [SerializeField]
        private float _acceleration = 10f;
        /// <summary>
        /// True if subscribed to events.
        /// </summary>
        private bool _subscribed = false;
        /// <summary>
        /// Used to move the rigidbody using prediction.
        /// </summary>
        private PredictionRigidbody _predictionRigidbody;

        private void Awake()
        {
            _predictionRigidbody = ResettableObjectCaches<PredictionRigidbody>.Retrieve();
            _predictionRigidbody.Initialize(GetComponent<Rigidbody>());
        }


        private void OnDestroy()
        {
            ResettableObjectCaches<PredictionRigidbody>.StoreAndDefault(ref _predictionRigidbody);
        }
        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (base.IsServerInitialized || base.Owner.IsLocalClient)
                SubscribeToEvents(true);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            SubscribeToEvents(false);
        }

        private void SubscribeToEvents(bool subscribe)
        {
            if (subscribe == _subscribed)
                return;
            if (base.TimeManager == null)
                return;

            _subscribed = subscribe;
            if (subscribe)
            {
                base.TimeManager.OnTick += TimeManager_OnTick;
                base.TimeManager.OnPostTick += TimeManager_OnPostTick;
            }
            else
            {
                base.TimeManager.OnTick -= TimeManager_OnTick;
                base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
            }
        }


        private void TimeManager_OnTick()
        {
            if (base.IsOwner)
            {
                CheckInput(out MoveData md);
                Move(md);
            }
            else if (base.IsServerInitialized)
            {
                Move(default);
            }
        }

        private void TimeManager_OnPostTick()
        {
            CreateReconcile();
        }

        private void CheckInput(out MoveData md)
        {
            md = default;

            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            if (horizontal == 0f && vertical == 0f)
                return;

            md = new MoveData(horizontal, vertical);
        }

        public override void CreateReconcile()
        {
            ReconcileData rd = new ReconcileData(_predictionRigidbody);
            Reconciliation(rd);
        }

        [Replicate]
        private void Move(MoveData md, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
        {
            Vector3 forces = new Vector3(md.Horizontal, 0f, md.Vertical) * _acceleration;
            if (forces != default)
                _predictionRigidbody.AddForce(forces);
            _predictionRigidbody.Simulate();
        }

        [Reconcile]
        private void Reconciliation(ReconcileData rd, Channel channel = Channel.Unreliable)
        {
            _predictionRigidbody.Reconcile(rd.PredictionRigidbody);
            _predictionRigidbody.Rigidbody.SetState(rd.RigidbodyState);
        }

    }


}