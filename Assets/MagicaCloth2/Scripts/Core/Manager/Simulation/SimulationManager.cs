// Magica Cloth 2.
// Copyright (c) 2023 MagicaSoft.
// https://magicasoft.jp
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MagicaCloth2
{
    public class SimulationManager : IManager, IValid
    {
        /// <summary>
        /// チームID
        /// </summary>
        public ExNativeArray<short> teamIdArray;

        /// <summary>
        /// 現在のシミュレーション座標
        /// </summary>
        public ExNativeArray<float3> nextPosArray;

        /// <summary>
        /// １つ前のシミュレーション座標
        /// </summary>
        public ExNativeArray<float3> oldPosArray;

        /// <summary>
        /// １つ前のシミュレーション回転(todo:現在未使用）
        /// </summary>
        public ExNativeArray<quaternion> oldRotArray;

        /// <summary>
        /// 現在のアニメーション姿勢座標
        /// カスタムスキニングの結果も反映されている
        /// </summary>
        public ExNativeArray<float3> basePosArray;

        /// <summary>
        /// 現在のアニメーション姿勢回転
        /// カスタムスキニングの結果も反映されている
        /// </summary>
        public ExNativeArray<quaternion> baseRotArray;

        /// <summary>
        /// １つ前の原点座標
        /// </summary>
        public ExNativeArray<float3> oldPositionArray;

        /// <summary>
        /// １つ前の原点回転
        /// </summary>
        public ExNativeArray<quaternion> oldRotationArray;

        /// <summary>
        /// 速度計算用座標
        /// </summary>
        public ExNativeArray<float3> velocityPosArray;

        /// <summary>
        /// 表示座標
        /// </summary>
        public ExNativeArray<float3> dispPosArray;

        /// <summary>
        /// 速度
        /// </summary>
        public ExNativeArray<float3> velocityArray;

        /// <summary>
        /// 実速度
        /// </summary>
        public ExNativeArray<float3> realVelocityArray;

        /// <summary>
        /// 摩擦(0.0 ~ 1.0)
        /// </summary>
        public ExNativeArray<float> frictionArray;

        /// <summary>
        /// 静止摩擦係数
        /// </summary>
        public ExNativeArray<float> staticFrictionArray;

        /// <summary>
        /// 接触コライダーの衝突法線
        /// </summary>
        public ExNativeArray<float3> collisionNormalArray;

        /// <summary>
        /// 接触中コライダーID
        /// 接触コライダーID+1が格納されているので注意！(0=なし)
        /// todo:現在未使用!
        /// </summary>
        //public ExNativeArray<int> colliderIdArray;

        public int ParticleCount => nextPosArray?.Count ?? 0;

        //=========================================================================================
        /// <summary>
        /// 制約
        /// </summary>
        public DistanceConstraint distanceConstraint;
        public TriangleBendingConstraint bendingConstraint;
        public TetherConstraint tetherConstraint;
        public AngleConstraint angleConstraint;
        public InertiaConstraint inertiaConstraint;
        public ColliderCollisionConstraint colliderCollisionConstraint;
        public MotionConstraint motionConstraint;
        public SelfCollisionConstraint selfCollisionConstraint;

        //=========================================================================================
        /// <summary>
        /// フレームもしくはステップごとに変動するリストを管理するための汎用バッファ。用途は様々
        /// 用于管理每帧或步骤变动的列表的通用缓冲器
        /// </summary>
        internal ExProcessingList<int> processingStepParticle;
        internal ExProcessingList<int> processingStepTriangleBending;
        internal ExProcessingList<int> processingStepEdgeCollision;
        internal ExProcessingList<int> processingStepCollider;
        internal ExProcessingList<int> processingStepBaseLine;
        //internal ExProcessingList<int> processingIntList5;
        internal ExProcessingList<int> processingStepMotionParticle;

        internal ExProcessingList<int> processingSelfParticle;
        internal ExProcessingList<uint> processingSelfPointTriangle;
        internal ExProcessingList<uint> processingSelfEdgeEdge;
        internal ExProcessingList<uint> processingSelfTrianglePoint;

        //---------------------------------------------------------------------
        /// <summary>
        /// 汎用float3作業バッファ
        /// </summary>
        internal NativeArray<float3> tempFloat3Buffer;

        /// <summary>
        /// パーティクルごとのfloat3集計カウンタ（排他制御用）
        /// </summary>
        internal NativeArray<int> countArray;

        /// <summary>
        /// パーティクルごとのfloat3蓄積リスト、内部は固定小数点。パーティクル数x3。（排他制御用）
        /// </summary>
        internal NativeArray<int> sumArray;

        /// <summary>
        /// ステップごとのシミュレーションの基準となる姿勢座標
        /// 初期姿勢とアニメーション姿勢をAnimatinBlendRatioで補間したもの
        /// </summary>
        public NativeArray<float3> stepBasicPositionBuffer;

        /// <summary>
        /// ステップごとのシミュレーションの基準となる姿勢回転
        /// 初期姿勢とアニメーション姿勢をAnimatinBlendRatioで補間したもの
        /// </summary>
        public NativeArray<quaternion> stepBasicRotationBuffer;

        /// <summary>
        /// ステップ実行カウンター
        /// </summary>
        internal int SimulationStepCount { get; private set; }

        /// <summary>
        /// 実行環境で利用できるワーカースレッド数
        /// </summary>
        internal int WorkerCount => Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount;

        bool isValid = false;

        //=========================================================================================
        public void Dispose()
        {
            isValid = false;

            teamIdArray?.Dispose();
            nextPosArray?.Dispose();
            oldPosArray?.Dispose();
            oldRotArray?.Dispose();
            basePosArray?.Dispose();
            baseRotArray?.Dispose();
            oldPositionArray?.Dispose();
            oldRotationArray?.Dispose();
            velocityPosArray?.Dispose();
            dispPosArray?.Dispose();
            velocityArray?.Dispose();
            realVelocityArray?.Dispose();
            frictionArray?.Dispose();
            staticFrictionArray?.Dispose();
            collisionNormalArray?.Dispose();
            //colliderIdArray?.Dispose();

            teamIdArray = null;
            nextPosArray = null;
            oldPosArray = null;
            oldRotArray = null;
            basePosArray = null;
            baseRotArray = null;
            oldPositionArray = null;
            oldRotationArray = null;
            velocityPosArray = null;
            dispPosArray = null;
            velocityArray = null;
            realVelocityArray = null;
            frictionArray = null;
            staticFrictionArray = null;
            collisionNormalArray = null;
            //colliderIdArray = null;

            processingStepParticle?.Dispose();
            processingStepTriangleBending?.Dispose();
            processingStepEdgeCollision?.Dispose();
            processingStepCollider?.Dispose();
            processingStepBaseLine?.Dispose();
            //processingIntList5?.Dispose();
            processingStepMotionParticle?.Dispose();
            processingSelfParticle?.Dispose();
            processingSelfPointTriangle?.Dispose();
            processingSelfEdgeEdge?.Dispose();
            processingSelfTrianglePoint?.Dispose();

            if (tempFloat3Buffer.IsCreated)
                tempFloat3Buffer.Dispose();
            if (countArray.IsCreated)
                countArray.Dispose();
            if (sumArray.IsCreated)
                sumArray.Dispose();
            if (stepBasicPositionBuffer.IsCreated)
                stepBasicPositionBuffer.Dispose();
            if (stepBasicRotationBuffer.IsCreated)
                stepBasicRotationBuffer.Dispose();

            distanceConstraint?.Dispose();
            bendingConstraint?.Dispose();
            tetherConstraint?.Dispose();
            angleConstraint?.Dispose();
            inertiaConstraint?.Dispose();
            colliderCollisionConstraint?.Dispose();
            motionConstraint?.Dispose();
            selfCollisionConstraint?.Dispose();
            distanceConstraint = null;
            bendingConstraint = null;
            tetherConstraint = null;
            angleConstraint = null;
            inertiaConstraint = null;
            colliderCollisionConstraint = null;
            motionConstraint = null;
            selfCollisionConstraint = null;
        }

        public void EnterdEditMode()
        {
            Dispose();
        }

        public void Initialize()
        {
            Dispose();

            const int capacity = 0; // 1024?
            teamIdArray = new ExNativeArray<short>(capacity);
            nextPosArray = new ExNativeArray<float3>(capacity);
            oldPosArray = new ExNativeArray<float3>(capacity);
            oldRotArray = new ExNativeArray<quaternion>(capacity);
            basePosArray = new ExNativeArray<float3>(capacity);
            baseRotArray = new ExNativeArray<quaternion>(capacity);
            oldPositionArray = new ExNativeArray<float3>(capacity);
            oldRotationArray = new ExNativeArray<quaternion>(capacity);
            velocityPosArray = new ExNativeArray<float3>(capacity);
            dispPosArray = new ExNativeArray<float3>(capacity);
            velocityArray = new ExNativeArray<float3>(capacity);
            realVelocityArray = new ExNativeArray<float3>(capacity);
            frictionArray = new ExNativeArray<float>(capacity);
            staticFrictionArray = new ExNativeArray<float>(capacity);
            collisionNormalArray = new ExNativeArray<float3>(capacity);
            //colliderIdArray = new ExNativeArray<int>(capacity);

            processingStepParticle = new ExProcessingList<int>();
            processingStepTriangleBending = new ExProcessingList<int>();
            processingStepEdgeCollision = new ExProcessingList<int>();
            processingStepCollider = new ExProcessingList<int>();
            processingStepBaseLine = new ExProcessingList<int>();
            //processingIntList5 = new ExProcessingList<int>();
            processingStepMotionParticle = new ExProcessingList<int>();
            processingSelfParticle = new ExProcessingList<int>();
            processingSelfPointTriangle = new ExProcessingList<uint>();
            processingSelfEdgeEdge = new ExProcessingList<uint>();
            processingSelfTrianglePoint = new ExProcessingList<uint>();

            tempFloat3Buffer = new NativeArray<float3>(capacity, Allocator.Persistent);

            // 制約
            distanceConstraint = new DistanceConstraint();
            bendingConstraint = new TriangleBendingConstraint();
            tetherConstraint = new TetherConstraint();
            angleConstraint = new AngleConstraint();
            inertiaConstraint = new InertiaConstraint();
            colliderCollisionConstraint = new ColliderCollisionConstraint();
            motionConstraint = new MotionConstraint();
            selfCollisionConstraint = new SelfCollisionConstraint();

            SimulationStepCount = 0;

            isValid = true;

            Develop.DebugLog($"JobWorkerCount:{Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount}");
            //Develop.DebugLog($"MaxJobThreadCount:{Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount}");
        }

        public bool IsValid()
        {
            return isValid;
        }

        //=========================================================================================
        /// <summary>
        /// プロキシメッシュをマネージャに登録する
        /// 在管理器中注册代理网格
        /// </summary>
        internal void RegisterProxyMesh(ClothProcess cprocess)
        {
            if (isValid == false)
                return;

            int teamId = cprocess.TeamId;
            var proxyMesh = cprocess.ProxyMeshContainer.shareVirtualMesh;
            ref var tdata = ref MagicaManager.Team.GetTeamDataRef(teamId);

            int pcnt = proxyMesh.VertexCount;
            tdata.particleChunk = teamIdArray.AddRange(pcnt, (short)teamId);
            nextPosArray.AddRange(pcnt);
            oldPosArray.AddRange(pcnt);
            oldRotArray.AddRange(pcnt);
            basePosArray.AddRange(pcnt);
            baseRotArray.AddRange(pcnt);
            oldPositionArray.AddRange(pcnt);
            oldRotationArray.AddRange(pcnt);
            velocityPosArray.AddRange(pcnt);
            dispPosArray.AddRange(pcnt);
            velocityArray.AddRange(pcnt);
            realVelocityArray.AddRange(pcnt);
            frictionArray.AddRange(pcnt);
            staticFrictionArray.AddRange(pcnt);
            collisionNormalArray.AddRange(pcnt);
            //colliderIdArray.AddRange(pcnt);
        }

        /// <summary>
        /// 制約データを登録する
        /// </summary>
        /// <param name="cprocess"></param>
        internal void RegisterConstraint(ClothProcess cprocess)
        {
            if (isValid == false)
                return;

            int teamId = cprocess.TeamId;

            // 慣性制約データをコピー（すでに領域は確保済みなのでコピーする）复制惯性约束数据（因为已经确保了区域所以复制）
            MagicaManager.Team.centerDataArray[teamId] = cprocess.inertiaConstraintData.centerData;

            // 制約データを登録する 注册约束数据
            distanceConstraint.Register(cprocess);
            bendingConstraint.Register(cprocess);
            inertiaConstraint.Register(cprocess);
            selfCollisionConstraint.Register(cprocess);
        }


        /// <summary>
        /// プロキシメッシュをマネージャから解除する
        /// 从管理器中取消代理网格
        /// </summary>
        internal void ExitProxyMesh(ClothProcess cprocess)
        {
            if (isValid == false)
                return;

            int teamId = cprocess.TeamId;
            ref var tdata = ref MagicaManager.Team.GetTeamDataRef(teamId);
            tdata.flag.SetBits(TeamManager.Flag_Exit, true); // 消滅フラグ

            var c = tdata.particleChunk;
            teamIdArray.RemoveAndFill(c);
            nextPosArray.Remove(c);
            oldPosArray.Remove(c);
            oldRotArray.Remove(c);
            basePosArray.Remove(c);
            baseRotArray.Remove(c);
            oldPositionArray.Remove(c);
            oldRotationArray.Remove(c);
            velocityPosArray.Remove(c);
            dispPosArray.Remove(c);
            velocityArray.Remove(c);
            realVelocityArray.Remove(c);
            frictionArray.Remove(c);
            staticFrictionArray.Remove(c);
            collisionNormalArray.Remove(c);
            //colliderIdArray.Remove(c);

            tdata.particleChunk.Clear();

            // 制約データを解除する
            distanceConstraint.Exit(cprocess);
            bendingConstraint.Exit(cprocess);
            inertiaConstraint.Exit(cprocess);
            selfCollisionConstraint.Exit(cprocess);
        }

        //=========================================================================================
        /// <summary>
        /// 更新工作缓冲区
        /// </summary>
        internal void WorkBufferUpdate()
        {
            int pcnt = ParticleCount;
            //int ecnt = MagicaManager.VMesh.EdgeCount;
            //int tcnt = MagicaManager.VMesh.TriangleCount;
            int bcnt = MagicaManager.VMesh.BaseLineCount;
            int ccnt = MagicaManager.Collider.DataCount;
            int bendCnt = bendingConstraint.DataCount;

            // 粒子的步骤处理
            processingStepParticle.UpdateBuffer(pcnt);

            // 三角形弯曲的步骤处理
            processingStepTriangleBending.UpdateBuffer(bendCnt);

            // 碰撞用边缘的步骤处理
            int edgeColliderCount = MagicaManager.Team.edgeColliderCollisionCount;
            processingStepEdgeCollision.UpdateBuffer(edgeColliderCount);

            // 处理碰撞器
            processingStepCollider.UpdateBuffer(ccnt);

            // 基线的步骤处理
            processingStepBaseLine.UpdateBuffer(bcnt);

            // 自碰撞粒子的步骤处理
            //processingIntList5.UpdateBuffer(pcnt);

            // 运动约束粒子的步骤执行
            processingStepMotionParticle.UpdateBuffer(pcnt);

            // 自碰撞
            processingSelfParticle.UpdateBuffer(pcnt);
            processingSelfPointTriangle.UpdateBuffer(selfCollisionConstraint.PointPrimitiveCount);
            processingSelfEdgeEdge.UpdateBuffer(selfCollisionConstraint.EdgePrimitiveCount);
            processingSelfTrianglePoint.UpdateBuffer(selfCollisionConstraint.TrianglePrimitiveCount);

            // 通用工作缓冲区
            tempFloat3Buffer.MC2Resize(pcnt);
            stepBasicPositionBuffer.MC2Resize(pcnt);
            stepBasicRotationBuffer.MC2Resize(pcnt);

            // 加法缓冲区
            countArray.MC2Resize(pcnt);
            sumArray.MC2Resize(pcnt * 3);

            // 约束
            angleConstraint.WorkBufferUpdate();
            colliderCollisionConstraint.WorkBufferUpdate();
            selfCollisionConstraint.WorkBufferUpdate();
        }

        //=========================================================================================
        /// <summary>
        /// シミュレーション実行前処理
        /// -リセット
        /// -移動影響
        /// </summary>
        /// <param name="jobHandle"></param>
        /// <returns></returns>
        internal JobHandle PreSimulationUpdate(JobHandle jobHandle)
        {
            // パーティクルのリセットおよび慣性の適用
            // 重置粒子并应用惯性
            var job = new PreSimulationUpdateJob()
            {
                teamDataArray = MagicaManager.Team.teamDataArray.GetNativeArray(),
                parameterArray = MagicaManager.Team.parameterArray.GetNativeArray(),
                centerDataArray = MagicaManager.Team.centerDataArray.GetNativeArray(),

                positions = MagicaManager.VMesh.positions.GetNativeArray(),
                rotations = MagicaManager.VMesh.rotations.GetNativeArray(),
                vertexDepths = MagicaManager.VMesh.vertexDepths.GetNativeArray(),

                teamIdArray = teamIdArray.GetNativeArray(),
                nextPosArray = nextPosArray.GetNativeArray(),
                oldPosArray = oldPosArray.GetNativeArray(),
                oldRotArray = oldRotArray.GetNativeArray(),
                basePosArray = basePosArray.GetNativeArray(),
                baseRotArray = baseRotArray.GetNativeArray(),
                oldPositionArray = oldPositionArray.GetNativeArray(),
                oldRotationArray = oldRotationArray.GetNativeArray(),
                velocityPosArray = velocityPosArray.GetNativeArray(),
                dispPosArray = dispPosArray.GetNativeArray(),
                velocityArray = velocityArray.GetNativeArray(),
                realVelocityArray = realVelocityArray.GetNativeArray(),
                frictionArray = frictionArray.GetNativeArray(),
                staticFrictionArray = staticFrictionArray.GetNativeArray(),
                collisionNormalArray = collisionNormalArray.GetNativeArray(),
                //colliderIdArray = colliderIdArray.GetNativeArray(),
            };
            jobHandle = job.Schedule(ParticleCount, 32, jobHandle);

            return jobHandle;
        }

        [BurstCompile]
        struct PreSimulationUpdateJob : IJobParallelFor
        {
            // team
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamManager.TeamData> teamDataArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<ClothParameters> parameterArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<InertiaConstraint.CenterData> centerDataArray;

            // vmesh
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> positions;
            [Unity.Collections.ReadOnly]
            public NativeArray<quaternion> rotations;
            [Unity.Collections.ReadOnly]
            public NativeArray<float> vertexDepths;

            // particle
            [Unity.Collections.ReadOnly]
            public NativeArray<short> teamIdArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> nextPosArray;
            public NativeArray<float3> oldPosArray;
            public NativeArray<quaternion> oldRotArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> basePosArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> baseRotArray;
            public NativeArray<float3> oldPositionArray;
            public NativeArray<quaternion> oldRotationArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> velocityPosArray;
            public NativeArray<float3> dispPosArray;
            public NativeArray<float3> velocityArray;
            public NativeArray<float3> realVelocityArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float> frictionArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float> staticFrictionArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> collisionNormalArray;
            //[Unity.Collections.WriteOnly]
            //public NativeArray<int> colliderIdArray;

            // パーティクルごと
            public void Execute(int pindex)
            {
                int teamId = teamIdArray[pindex];
                if (teamId == 0)
                    return;

                var tdata = teamDataArray[teamId];
                if (tdata.IsProcess == false)
                    return;

                int l_index = pindex - tdata.particleChunk.startIndex;
                int vindex = tdata.proxyCommonChunk.startIndex + l_index;


                if (tdata.IsReset)
                {
                    // リセット
                    var pos = positions[vindex];
                    var rot = rotations[vindex];

                    nextPosArray[pindex] = pos;
                    oldPosArray[pindex] = pos;
                    oldRotArray[pindex] = rot;
                    basePosArray[pindex] = pos;
                    baseRotArray[pindex] = rot;
                    oldPositionArray[pindex] = pos;
                    oldRotationArray[pindex] = rot;
                    velocityPosArray[pindex] = pos;
                    dispPosArray[pindex] = pos;
                    velocityArray[pindex] = 0;
                    realVelocityArray[pindex] = 0;
                    frictionArray[pindex] = 0;
                    staticFrictionArray[pindex] = 0;
                    collisionNormalArray[pindex] = 0;
                    //colliderIdArray[pindex] = 0;
                }
                else if (tdata.IsInertiaShift || tdata.IsNegativeScaleTeleport)
                {
                    var cdata = centerDataArray[teamId];

                    var oldPos = oldPosArray[pindex];
                    var oldRot = oldRotArray[pindex];
                    var oldPosition = oldPositionArray[pindex];
                    var oldRotation = oldRotationArray[pindex];
                    var dispPos = dispPosArray[pindex];
                    var velocity = velocityArray[pindex];
                    var realVelocity = realVelocityArray[pindex];

                    // TODO 用来测试效果
                    // Debug.Log("OldPos: " + oldPos + " CurrentPos: " + positions[vindex] + "CurrentPos2: " + positions[pindex]);

                    // ■マイナススケール 负数
                    if (tdata.IsNegativeScaleTeleport)
                    {
                        // 本体のスケール反転に合わせてシミュレーションに影響が出ないように必要な座標系を同様に軸反転させる
                        // パーティクルはセンター空間で軸反転させる
                        // 軸反転用マトリックス

                        // 为了配合主体的缩放反转不影响模拟，同样使必要的坐标系轴反转
                        //粒子在中心空间轴翻转
                        //轴反转用矩阵
                        float4x4 negativeM = cdata.negativeScaleMatrix;

                        oldPos = MathUtility.TransformPoint(oldPos, negativeM);
                        oldRot = MathUtility.TransformRotation(oldRot, negativeM, 1);

                        oldPosition = MathUtility.TransformPoint(oldPosition, negativeM);
                        oldRotation = MathUtility.TransformRotation(oldRotation, negativeM, 1);

                        dispPos = MathUtility.TransformPoint(dispPos, negativeM);

                        velocity = MathUtility.TransformVector(velocity, negativeM);
                        realVelocity = MathUtility.TransformVector(realVelocity, negativeM);
                    }

                    // ■慣性全体シフト
                    // 惯性整体位移
                    if (tdata.IsInertiaShift)
                    {
                        // cdata.frameComponentShiftVector : 全体シフトベクトル
                        // cdata.frameComponentShiftRotation : 全体シフト回転
                        // cdata.oldComponentWorldPosition : フレーム移動前のコンポーネント中心位置

                        oldPos = MathUtility.ShiftPosition(oldPos, cdata.oldComponentWorldPosition, cdata.frameComponentShiftVector, cdata.frameComponentShiftRotation);
                        oldRot = math.mul(cdata.frameComponentShiftRotation, oldRot);

                        oldPosition = MathUtility.ShiftPosition(oldPosition, cdata.oldComponentWorldPosition, cdata.frameComponentShiftVector, cdata.frameComponentShiftRotation);
                        oldRotation = math.mul(cdata.frameComponentShiftRotation, oldRotation);

                        dispPos = MathUtility.ShiftPosition(dispPos, cdata.oldComponentWorldPosition, cdata.frameComponentShiftVector, cdata.frameComponentShiftRotation);

                        velocity = math.mul(cdata.frameComponentShiftRotation, velocity);
                        realVelocity = math.mul(cdata.frameComponentShiftRotation, realVelocity);
                    }

                    oldPosArray[pindex] = oldPos;
                    oldRotArray[pindex] = oldRot;
                    oldPositionArray[pindex] = oldPosition;
                    oldRotationArray[pindex] = oldRotation;
                    dispPosArray[pindex] = dispPos;
                    velocityArray[pindex] = velocity;
                    realVelocityArray[pindex] = realVelocity;
                }
            }
        }

        //=========================================================================================
        /// <summary>
        /// クロスシミュレーションの１ステップ実行
        /// 交叉模拟的一步执行
        /// </summary>
        /// <param name="updateCount"></param>
        /// <param name="updateIndex"></param>
        /// <param name="simulationDeltaTime"></param>
        /// <param name="jobHandle"></param>
        /// <returns></returns>
        unsafe internal JobHandle SimulationStepUpdate(int updateCount, int updateIndex, JobHandle jobHandle)
        {
            //Debug.Log($"Step:{updateIndex}/{updateCount}");

            var tm = MagicaManager.Team;
            var vm = MagicaManager.VMesh;
            var wm = MagicaManager.Wind;

            // シミュレーションステップカウンター
            // 模拟步进计数器
            SimulationStepCount++;

            // ステップごとのチーム更新
            // 每个步骤的团队更新
            jobHandle = tm.SimulationStepTeamUpdate(updateIndex, jobHandle);

            // 今回のステップで計算が必要な作業リストを作成する
            // 在本次步骤中制作需要计算的作业列表
            var clearStepCounterJob = new ClearStepCounter()
            {
                processingStepParticle = processingStepParticle.Counter, // 步进粒子
                processingStepTriangleBending = processingStepTriangleBending.Counter, // 步进执行三角折弯
                processingStepEdgeCollision = processingStepEdgeCollision.Counter, // 步进执行边碰撞
                processingStepCollider = processingStepCollider.Counter, // 步骤执行协作器列表
                processingStepBaseLine = processingStepBaseLine.Counter, // 步进执行基线
                //processingCounter5 = processingIntList5.Counter, // (reserve)
                processingStepMotionParticle = processingStepMotionParticle.Counter, // 步长执行运动约束粒子

                processingSelfParticle = processingSelfParticle.Counter,
                processingSelfPointTriangle = processingSelfPointTriangle.Counter,
                processingSelfEdgeEdge = processingSelfEdgeEdge.Counter,
                processingSelfTrianglePoint = processingSelfTrianglePoint.Counter,
            };
            jobHandle = clearStepCounterJob.Schedule(jobHandle);

            var createUpdateParticleJob = new CreateUpdateParticleList()
            {
                teamDataArray = tm.teamDataArray.GetNativeArray(),
                parameterArray = tm.parameterArray.GetNativeArray(),

                stepParticleIndexCounter = processingStepParticle.Counter,
                stepParticleIndexArray = processingStepParticle.Buffer,

                stepBaseLineIndexCounter = processingStepBaseLine.Counter,
                stepBaseLineIndexArray = processingStepBaseLine.Buffer,

                stepTriangleBendIndexCounter = processingStepTriangleBending.Counter,
                stepTriangleBendIndexArray = processingStepTriangleBending.Buffer,

                stepEdgeCollisionIndexCounter = processingStepEdgeCollision.Counter,
                stepEdgeCollisionIndexArray = processingStepEdgeCollision.Buffer,

                motionParticleIndexCounter = processingStepMotionParticle.Counter,
                motionParticleIndexArray = processingStepMotionParticle.Buffer,

                selfParticleCounter = processingSelfParticle.Counter,
                selfParticleIndexArray = processingSelfParticle.Buffer,
                selfPointTriangleCounter = processingSelfPointTriangle.Counter,
                selfPointTriangleIndexArray = processingSelfPointTriangle.Buffer,
                selfEdgeEdgeCounter = processingSelfEdgeEdge.Counter,
                selfEdgeEdgeIndexArray = processingSelfEdgeEdge.Buffer,
                selfTrianglePointCounter = processingSelfTrianglePoint.Counter,
                selfTrianglePointIndexArray = processingSelfTrianglePoint.Buffer,
            };
            jobHandle = createUpdateParticleJob.Schedule(tm.TeamCount, 1, jobHandle);

            // 今回のステップで計算が必要なコライダーリストを作成する
            // 在此步骤中创建需要计算的协作者列表
            jobHandle = MagicaManager.Collider.CreateUpdateColliderList(updateIndex, jobHandle);

            // コライダーの更新
            // 更新协作者
            jobHandle = MagicaManager.Collider.StartSimulationStep(jobHandle);

            // 速度更新、外力の影響、慣性シフト
            // 速度更新、外力影响、惯性偏移   *****  把nextPos最终解算位置 赋值到
            var startStepJob = new StartSimulationStepJob()
            {
                simulationPower = MagicaManager.Time.SimulationPower,
                simulationDeltaTime = MagicaManager.Time.SimulationDeltaTime,

                stepParticleIndexArray = processingStepParticle.Buffer,

                attributes = vm.attributes.GetNativeArray(),
                depthArray = vm.vertexDepths.GetNativeArray(),
                positions = vm.positions.GetNativeArray(),
                rotations = vm.rotations.GetNativeArray(),
                vertexRootIndices = vm.vertexRootIndices.GetNativeArray(),

                teamDataArray = tm.teamDataArray.GetNativeArray(),
                parameterArray = tm.parameterArray.GetNativeArray(),
                centerDataArray = tm.centerDataArray.GetNativeArray(),
                teamWindArray = tm.teamWindArray.GetNativeArray(),

                windDataArray = wm.windDataArray.GetNativeArray(),

                teamIdArray = teamIdArray.GetNativeArray(),
                oldPosArray = oldPosArray.GetNativeArray(),
                velocityArray = velocityArray.GetNativeArray(),
                nextPosArray = nextPosArray.GetNativeArray(),
                basePosArray = basePosArray.GetNativeArray(),
                baseRotArray = baseRotArray.GetNativeArray(),
                oldPositionArray = oldPositionArray.GetNativeArray(),
                oldRotationArray = oldRotationArray.GetNativeArray(),
                velocityPosArray = velocityPosArray.GetNativeArray(),
                frictionArray = frictionArray.GetNativeArray(),

                stepBasicPositionArray = stepBasicPositionBuffer,
                stepBasicRotationArray = stepBasicRotationBuffer,
            };
            jobHandle = startStepJob.Schedule(processingStepParticle.GetJobSchedulePtr(), 32, jobHandle);

            // 制約解決のためのステップごとの基準姿勢を計算（ベースラインから）
            // 计算用于解决限制的每个步骤的基准姿势（从基线开始）
            var updateStepBasicPotureJob = new UpdateStepBasicPotureJob()
            {
                stepBaseLineIndexArray = processingStepBaseLine.Buffer,

                teamDataArray = tm.teamDataArray.GetNativeArray(),

                attributes = MagicaManager.VMesh.attributes.GetNativeArray(),
                vertexParentIndices = vm.vertexParentIndices.GetNativeArray(),
                vertexLocalPositions = vm.vertexLocalPositions.GetNativeArray(),
                vertexLocalRotations = vm.vertexLocalRotations.GetNativeArray(),
                baseLineStartDataIndices = vm.baseLineStartDataIndices.GetNativeArray(),
                baseLineDataCounts = vm.baseLineDataCounts.GetNativeArray(),
                baseLineData = vm.baseLineData.GetNativeArray(),
                //vertexToTransformRotations = vm.vertexToTransformRotations.GetNativeArray(),

                basePosArray = basePosArray.GetNativeArray(),
                baseRotArray = baseRotArray.GetNativeArray(),

                stepBasicPositionArray = stepBasicPositionBuffer,
                stepBasicRotationArray = stepBasicRotationBuffer,
            };
            jobHandle = updateStepBasicPotureJob.Schedule(processingStepBaseLine.GetJobSchedulePtr(), 2, jobHandle);

            // 制約の解決
            //for (int i = 0; i < 2; i++)
            {
                // 一般制約
                jobHandle = tetherConstraint.SolverConstraint(jobHandle);
                jobHandle = distanceConstraint.SolverConstraint(jobHandle);
                jobHandle = angleConstraint.SolverConstraint(jobHandle);
                jobHandle = bendingConstraint.SolverConstraint(jobHandle);
                // コライダーコリジョン
                jobHandle = colliderCollisionConstraint.SolverConstraint(jobHandle);
                // コライダー衝突後はパーティクルが乱れる可能性があるためもう一度距離制約で整える。
                // これは裏返り防止などに効果大。
                // 由于协作器碰撞后粒子可能会混乱，所以再次通过距离限制调整。
                // 这对防止翻身等效果很好。
                jobHandle = distanceConstraint.SolverConstraint(jobHandle);
                // モーション制約はコライダーより優先
                jobHandle = motionConstraint.SolverConstraint(jobHandle);
                // セルフコリジョンは最後
                jobHandle = selfCollisionConstraint.SolverConstraint(updateIndex, jobHandle);
            }

            // 座標確定 nextPos会赋值给oldPosArray
            var endStepJob = new EndSimulationStepJob()
            {
                simulationDeltaTime = MagicaManager.Time.SimulationDeltaTime,

                stepParticleIndexArray = processingStepParticle.Buffer,

                teamDataArray = tm.teamDataArray.GetNativeArray(),
                parameterArray = tm.parameterArray.GetNativeArray(),
                centerDataArray = tm.centerDataArray.GetNativeArray(),

                attributes = vm.attributes.GetNativeArray(),
                vertexDepths = vm.vertexDepths.GetNativeArray(),

                teamIdArray = teamIdArray.GetNativeArray(),
                nextPosArray = nextPosArray.GetNativeArray(),
                oldPosArray = oldPosArray.GetNativeArray(), //最终的赋值
                velocityArray = velocityArray.GetNativeArray(),
                realVelocityArray = realVelocityArray.GetNativeArray(),
                velocityPosArray = velocityPosArray.GetNativeArray(),
                frictionArray = frictionArray.GetNativeArray(),
                staticFrictionArray = staticFrictionArray.GetNativeArray(),
                collisionNormalArray = collisionNormalArray.GetNativeArray(),
            };
            jobHandle = endStepJob.Schedule(processingStepParticle.GetJobSchedulePtr(), 32, jobHandle);

            // コライダーの後更新
            jobHandle = MagicaManager.Collider.EndSimulationStep(jobHandle);

            return jobHandle;
        }

        [BurstCompile]
        struct ClearStepCounter : IJob
        {
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingStepParticle;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingStepTriangleBending;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingStepEdgeCollision;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingStepCollider;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingStepBaseLine;
            //[Unity.Collections.WriteOnly]
            //public NativeReference<int> processingCounter5;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingStepMotionParticle;

            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingSelfParticle;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingSelfPointTriangle;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingSelfEdgeEdge;
            [Unity.Collections.WriteOnly]
            public NativeReference<int> processingSelfTrianglePoint;

            public void Execute()
            {
                processingStepParticle.Value = 0;
                processingStepTriangleBending.Value = 0;
                processingStepEdgeCollision.Value = 0;
                processingStepCollider.Value = 0;
                processingStepBaseLine.Value = 0;
                //processingCounter5.Value = 0;
                processingStepMotionParticle.Value = 0;

                processingSelfParticle.Value = 0;
                processingSelfPointTriangle.Value = 0;
                processingSelfEdgeEdge.Value = 0;
                processingSelfTrianglePoint.Value = 0;
            }
        }

        [BurstCompile]
        struct CreateUpdateParticleList : IJobParallelFor
        {
            // team
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamManager.TeamData> teamDataArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<ClothParameters> parameterArray;

            // buffer
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> stepParticleIndexCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<int> stepParticleIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> stepBaseLineIndexCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<int> stepBaseLineIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> stepTriangleBendIndexCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<int> stepTriangleBendIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> stepEdgeCollisionIndexCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<int> stepEdgeCollisionIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> motionParticleIndexCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<int> motionParticleIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> selfParticleCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<int> selfParticleIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> selfPointTriangleCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<uint> selfPointTriangleIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> selfEdgeEdgeCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<uint> selfEdgeEdgeIndexArray;

            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeReference<int> selfTrianglePointCounter;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<uint> selfTrianglePointIndexArray;

            // チームごと
            public void Execute(int teamId)
            {
                if (teamId == 0)
                    return;

                var tdata = teamDataArray[teamId];
                if (tdata.IsProcess == false || tdata.IsRunning == false)
                    return;

                // このステップでの更新があるか判定する
                // 判定是否有该步骤中更新
                if (tdata.IsStepRunning == false)
                    return;

                var parameter = parameterArray[teamId];

                // パーティクルリスト
                // 粒子列表
                int pcnt = tdata.particleChunk.dataLength;
                int pstart = tdata.particleChunk.startIndex;
                int start = stepParticleIndexCounter.MC2InterlockedStartIndex(pcnt);
                for (int i = 0; i < pcnt; i++)
                {
                    stepParticleIndexArray[start + i] = pstart + i;
                }

                // ベースライン
                // 基线
                int bcnt = tdata.BaseLineCount;
                int bstart = tdata.baseLineChunk.startIndex;
                start = stepBaseLineIndexCounter.MC2InterlockedStartIndex(bcnt);
                for (int i = 0; i < bcnt; i++)
                {
                    // 上位16bit:チームID, 下位16bit:ベースラインインデックス
                    uint pack = DataUtility.Pack32(teamId, bstart + i);
                    stepBaseLineIndexArray[start + i] = (int)pack;
                }

                // トライアングルベンド
                // 三角弯头
                if (parameter.triangleBendingConstraint.method != TriangleBendingConstraint.Method.None)
                {
                    int bendCnt = tdata.bendingPairChunk.dataLength;
                    int bendIndex = tdata.bendingPairChunk.startIndex;
                    start = stepTriangleBendIndexCounter.MC2InterlockedStartIndex(bendCnt);
                    for (int i = 0; i < bendCnt; i++, bendIndex++)
                    {
                        uint pack = DataUtility.Pack12_20(teamId, bendIndex);
                        stepTriangleBendIndexArray[start + i] = (int)pack;
                    }
                }

                // エッジコライダーコリジョン
                // 边缘胶合器碰撞
                int colliderCount = tdata.ColliderCount;
                if (parameter.colliderCollisionConstraint.mode == ColliderCollisionConstraint.Mode.Edge && tdata.proxyEdgeChunk.IsValid && colliderCount > 0)
                {
                    int ecnt = tdata.proxyEdgeChunk.dataLength;
                    int estart = tdata.proxyEdgeChunk.startIndex;
                    start = stepEdgeCollisionIndexCounter.MC2InterlockedStartIndex(ecnt);
                    for (int i = 0; i < ecnt; i++)
                    {
                        stepEdgeCollisionIndexArray[start + i] = estart + i;
                    }
                }

                // モーション制約パーティクル
                // 运动约束粒子
                if (parameter.motionConstraint.useMaxDistance || parameter.motionConstraint.useBackstop)
                {
                    start = motionParticleIndexCounter.MC2InterlockedStartIndex(pcnt);
                    for (int i = 0; i < pcnt; i++)
                    {
                        motionParticleIndexArray[start + i] = pstart + i;
                    }
                }

                // セルフコリジョン
                // 自碰撞
                bool useSelfEdgeEdge = tdata.flag.TestAny(TeamManager.Flag_Self_EdgeEdge, 3);
                bool useSelfPointTriangle = tdata.flag.TestAny(TeamManager.Flag_Self_PointTriangle, 3);
                bool useSelfTrianglePoint = tdata.flag.TestAny(TeamManager.Flag_Self_TrianglePoint, 3);
                if (useSelfEdgeEdge)
                {
                    int ecnt = tdata.EdgeCount;
                    start = selfEdgeEdgeCounter.MC2InterlockedStartIndex(ecnt);
                    for (int i = 0; i < ecnt; i++)
                    {
                        // 上位16bit:チームID, 下位16bit:Edgeインデックス
                        uint pack = DataUtility.Pack32(teamId, i);
                        selfEdgeEdgeIndexArray[start + i] = pack;
                    }
                }
                if (useSelfPointTriangle)
                {
                    start = selfPointTriangleCounter.MC2InterlockedStartIndex(pcnt);
                    for (int i = 0; i < pcnt; i++)
                    {
                        // 上位16bit:チームID, 下位16bit:Pointインデックス
                        uint pack = DataUtility.Pack32(teamId, i);
                        selfPointTriangleIndexArray[start + i] = pack;
                    }
                }
                if (useSelfTrianglePoint)
                {
                    int tcnt = tdata.TriangleCount;
                    start = selfTrianglePointCounter.MC2InterlockedStartIndex(tcnt);
                    for (int i = 0; i < tcnt; i++)
                    {
                        // 上位16bit:チームID, 下位16bit:Triangleインデックス
                        uint pack = DataUtility.Pack32(teamId, i);
                        selfTrianglePointIndexArray[start + i] = pack;
                    }
                }
                if (useSelfEdgeEdge || useSelfPointTriangle || useSelfTrianglePoint)
                {
                    start = selfParticleCounter.MC2InterlockedStartIndex(pcnt);
                    for (int i = 0; i < pcnt; i++)
                    {
                        selfParticleIndexArray[start + i] = pstart + i;
                    }
                }

                //Debug.Log($"Step:{updateIndex}, updateParticleCount:{jobParticleIndexList.Length}");
            }
        }

        [BurstCompile]
        struct StartSimulationStepJob : IJobParallelForDefer
        {
            public float4 simulationPower;
            public float simulationDeltaTime;

            [Unity.Collections.ReadOnly]
            public NativeArray<int> stepParticleIndexArray;

            // vmesh
            [Unity.Collections.ReadOnly]
            public NativeArray<VertexAttribute> attributes;
            [Unity.Collections.ReadOnly]
            public NativeArray<float> depthArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> positions;
            [Unity.Collections.ReadOnly]
            public NativeArray<quaternion> rotations;
            [Unity.Collections.ReadOnly]
            public NativeArray<int> vertexRootIndices;

            // team
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamManager.TeamData> teamDataArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<ClothParameters> parameterArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<InertiaConstraint.CenterData> centerDataArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamWindData> teamWindArray;

            // wind
            [Unity.Collections.ReadOnly]
            public NativeArray<WindManager.WindData> windDataArray;

            // particle
            [Unity.Collections.ReadOnly]
            public NativeArray<short> teamIdArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> oldPosArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> velocityArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> nextPosArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> basePosArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> baseRotArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> oldPositionArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<quaternion> oldRotationArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> velocityPosArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float> frictionArray;

            // buffer
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> stepBasicPositionArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> stepBasicRotationArray;


            // ステップパーティクルごと 每步粒子
            public void Execute(int index)
            {
                int pindex = stepParticleIndexArray[index];
                int teamId = teamIdArray[pindex];
                var tdata = teamDataArray[teamId];
                int l_index = pindex - tdata.particleChunk.startIndex;

                // 各カテゴリのデータインデックスjmn
                // 每个类别的数据索引
                int vindex = tdata.proxyCommonChunk.startIndex + l_index;

                // パラメータ 参数
                var param = parameterArray[teamId];

                // nextPosSwap
                var attr = attributes[vindex];
                float depth = depthArray[vindex];
                var oldPos = oldPosArray[pindex];

                var nextPos = oldPos;
                var velocityPos = oldPos;

                // 基準姿勢のステップ補間
                // 基准姿势步进插补
                var oldPosition = oldPositionArray[pindex];
                var oldRotation = oldRotationArray[pindex];
                var position = positions[vindex];
                var rotation = rotations[vindex];

                // ベース位置補間
                // 基极位置插补
                float3 basePos = math.lerp(oldPosition, position, tdata.frameInterpolation);
                quaternion baseRot = math.slerp(oldRotation, rotation, tdata.frameInterpolation);
                baseRot = math.normalize(baseRot); // 必要
                basePosArray[pindex] = basePos;
                baseRotArray[pindex] = baseRot;

                // 步骤基本位置
                stepBasicPositionArray[pindex] = basePos;
                stepBasicRotationArray[pindex] = baseRot;

                // 移动粒子
                if (attr.IsMove() || tdata.IsSpring)
                {
                    var cdata = centerDataArray[teamId];

                    // 重量
                    //float mass = MathUtility.CalcMass(depth);

                    // 速度
                    var velocity = velocityArray[pindex];

#if true
                    // ■ローカル慣性シフト
                    // シフト量
                    // ■局部惯性偏移
                    // 移位量
                    float3 inertiaVector = cdata.inertiaVector;
                    quaternion inertiaRotation = cdata.inertiaRotation;

                    // 慣性の深さ影響
                    // 惯性深度影响
                    float inertiaDepth = param.inertiaConstraint.depthInertia * (1.0f - depth * depth); // 二次曲線
                    //Debug.Log($"[{pindex}] inertiaDepth:{inertiaDepth}");
                    inertiaVector = math.lerp(inertiaVector, cdata.stepVector, inertiaDepth);
                    inertiaRotation = math.slerp(inertiaRotation, cdata.stepRotation, inertiaDepth);

                    //Debug.Log($"[{pindex}] depthInertia:{inertiaDepth} stepVector {cdata.stepVector} inertiaVector:{inertiaVector} inertiaDepth:{inertiaDepth}");

                    // たぶんこっちが正しい
                    // 也许这是对的
                    float3 lpos = oldPos - cdata.oldWorldPosition;
                    lpos = math.mul(inertiaRotation, lpos);
                    lpos += inertiaVector;
                    float3 wpos = cdata.oldWorldPosition + lpos;
                    var inertiaOffset = wpos - nextPos;

                    // nextPos
                    nextPos = wpos;

                    // 速度位置も調整
                    velocityPos += inertiaOffset;

                    // 速度に慣性回転を加える
                    // 速度加上惯性旋转
                    velocity = math.mul(inertiaRotation, velocity);
#endif

                    // 安定化用の速度割合
                    velocity *= tdata.velocityWeight;

                    // 抵抗
                    // 重力に影響させたくないので先に計算する（※通常はforce適用後に行うのが一般的）
                    // 因为不想影响重力所以先计算（※通常force一般在适用后进行
                    float damping = param.dampingCurveData.MC2EvaluateCurveClamp01(depth);
                    velocity *= math.saturate(1.0f - damping * simulationPower.z);

                    // 外力
                    float3 force = 0;

                    // 重力
                    float3 gforce = param.worldGravityDirection * (param.gravity * tdata.gravityRatio);
                    force += gforce;

                    //Debug.Log($"[{pindex}] 查看重力计算参数: fordeMode:{tdata.forceMode.ToString()} worldGravityDirection：{param.worldGravityDirection} gravity {param.gravity} gravityRatio:{tdata.gravityRatio} ");

                    // 外力
                    float3 exForce = 0;
                    float mass = MathUtility.CalcMass(depth);
                    switch (tdata.forceMode)
                    {
                        case ClothForceMode.VelocityAdd:
                            exForce = tdata.impactForce / mass;
                            break;
                        case ClothForceMode.VelocityAddWithoutDepth:
                            exForce = tdata.impactForce;
                            break;
                        case ClothForceMode.VelocityChange:
                            exForce = tdata.impactForce / mass;
                            velocity = 0;
                            break;
                        case ClothForceMode.VelocityChangeWithoutDepth:
                            exForce = tdata.impactForce;
                            velocity = 0;
                            break;
                    }
                    force += exForce;

                    // 風力
                    force += Wind(teamId, tdata, param.wind, cdata, vindex, pindex, depth);

                    // 外力チームスケール倍率
                    force *= tdata.scaleRatio;

                    // 速度更新
                    velocity += force * simulationDeltaTime;

                    // 予測位置更新
                    nextPos += velocity * simulationDeltaTime;
                }
                else
                {
                    // 固定パーティクル
                    nextPos = basePos;
                    velocityPos = basePos;
                }

                // スプリング（固定パーティクルのみ）
                if (tdata.IsSpring && attr.IsFixed())
                {
                    // ノイズ用に時間を不規則にずらす
                    Spring(param.springConstraint, param.normalAxis, ref nextPos, basePos, baseRot, (tdata.time + index * 49.6198f) * 2.4512f + math.csum(nextPos), tdata.scaleRatio);
                }

                // 速度計算用の移動前の位置
                // 用于速度计算的移动前的位置
                velocityPosArray[pindex] = velocityPos;

                // 予測位置格納
                nextPosArray[pindex] = nextPos;
            }

            void Spring(in SpringConstraint.SpringConstraintParams springParams, ClothNormalAxis normalAxis, ref float3 nextPos, in float3 basePos, in quaternion baseRot, float noiseTime, float scaleRatio)
            {
                // clamp distance
                var v = nextPos - basePos;
                float3 dir = math.up();
                switch (normalAxis)
                {
                    case ClothNormalAxis.Right:
                        dir = math.right();
                        break;
                    case ClothNormalAxis.Up:
                        dir = math.up();
                        break;
                    case ClothNormalAxis.Forward:
                        dir = math.forward();
                        break;
                    case ClothNormalAxis.InverseRight:
                        dir = -math.right();
                        break;
                    case ClothNormalAxis.InverseUp:
                        dir = -math.up();
                        break;
                    case ClothNormalAxis.InverseForward:
                        dir = -math.forward();
                        break;
                }
                dir = math.mul(baseRot, dir);
                float limitDistance = springParams.limitDistance * scaleRatio; // スケール倍率

                if (limitDistance > 1e-08f)
                {
                    // 球クランプ
                    var len = math.length(v);
                    if (len > limitDistance)
                    {
                        v *= (limitDistance / len);
                    }

                    // 楕円クランプ
                    if (springParams.normalLimitRatio < 1.0f)
                    {
                        // もっとスマートにならないか..
                        float ylen = math.dot(dir, v);
                        float3 vx = v - dir * ylen;
                        float xlen = math.length(vx);
                        float t = xlen / limitDistance;
                        float y = math.cos(math.asin(t));
                        y *= limitDistance * springParams.normalLimitRatio;

                        if (math.abs(ylen) > y)
                        {
                            v -= dir * (math.abs(ylen) - y) * math.sign(ylen);
                        }
                    }
                }
                else
                {
                    v = float3.zero;
                }

                // スプリング力
                float power = springParams.springPower;

                // ノイズ
                if (springParams.springNoise > 0.0f)
                {
                    float noise = math.sin(noiseTime); // -1.0~+1.0
                    //Debug.Log(noise);
                    noise *= springParams.springNoise * 0.6f; // スケーリング
                    power = math.max(power + power * noise, 0.0f);
                }

                // スプリング適用
                v -= v * power;
                nextPos = basePos + v;
            }


            float3 Wind(int teamId, in TeamManager.TeamData tdata, in WindParams windParams, in InertiaConstraint.CenterData cdata, int vindex, int pindex, float depth)
            {
                float3 windForce = 0;

                // 基準ルート座標
                // (1)チームごとにずらす
                // (2)同期率によりルートラインごとにずらす
                // (3)チームの座標やパーティクルの座標は計算に入れない
                int rootIndex = vertexRootIndices[vindex];
                float3 windPos = (teamId + 1) * 4.19230645f + (rootIndex * 0.0023963f * (1.0f - windParams.synchronization) * 100);

                // ゾーンごとの風影響計算
                var teamWindData = teamWindArray[teamId];
                int cnt = teamWindData.ZoneCount;
                for (int i = 0; i < cnt; i++)
                {
                    var windInfo = teamWindData.windZoneList[i];
                    var windData = windDataArray[windInfo.windId];
                    windForce += WindForceBlend(windInfo, windParams, windPos, windData.turbulence);
                }

#if true
                // 移動風影響計算
                if (windParams.movingWind > 0.01f)
                {
                    windForce += WindForceBlend(teamWindData.movingWind, windParams, windPos, 1.0f);
                }
#endif

                //Debug.Log($"windForce:{windForce}");

                // その他影響
                // チーム風影響
                float influence = windParams.influence; // 0.0 ~ 2.0

                // 摩擦による影響
                float friction = frictionArray[pindex];
                influence *= (1.0f - friction);

                // 深さ影響
                float depthScale = depth * depth;
                influence *= math.lerp(1.0f, depthScale, windParams.depthWeight);

                // 最終影響
                windForce *= influence;

                //Debug.Log($"windForce:{windForce}");

                return windForce;
            }

            float3 WindForceBlend(in TeamWindInfo windInfo, in WindParams windParams, in float3 windPos, float windTurbulence)
            {
                float windMain = windInfo.main;
                if (windMain < 0.01f)
                    return 0;

                //Debug.Log($"windMain:{windMain}");

                // 風速係数
                float mainRatio = windMain / Define.System.WindBaseSpeed; // 0.0 ~ 

                // Sin波形
                var sinPos = windPos + windInfo.time * 10.0f;
                float2 sinXY = math.sin(sinPos.xy);

                // Noise波形
                var noisePos = windPos + windInfo.time * 2.3132f; // Sin波形との調整用
                float2 noiseXY = new float2(noise.cnoise(noisePos.xy), noise.cnoise(noisePos.yx));
                noiseXY *= 2.3f; // cnoiseは弱いので補強 2.0?

                // 波形ブレンド
                float2 waveXY = math.lerp(sinXY, noiseXY, windParams.blend);

                // 基本乱流率
                windTurbulence *= windParams.turbulence; // 0.0 ~ 2.0

                // 風向き
                const float rangAng = 45.0f; // 乱流角度
                var ang = math.radians(waveXY * rangAng);
                ang.y *= math.lerp(0.1f, 0.5f, windParams.blend); // 横方向は抑える。そうしないと円運動になってしまうため。0.3 - 0.5?
                ang *= windTurbulence; // 乱流率
                var rq = quaternion.Euler(ang.x, ang.y, 0.0f); // XY
                var dirq = MathUtility.AxisQuaternion(windInfo.direction);
                float3 wdir = math.forward(math.mul(dirq, rq));

                // 風速
                // 風速が低いと大きくなり、風速が高いと0.0になる
                float mainScale = math.saturate(1.0f - mainRatio * 1.0f);
                float mainWave = math.unlerp(-1.0f, 1.0f, waveXY.x); // 0.0 ~ 1.0
                mainWave *= mainScale * windTurbulence;
                windMain -= windMain * mainWave;

                // 合成
                float3 windForce = wdir * windMain;

                return windForce;
            }
        }

        /// <summary>
        /// ベースラインごとに初期姿勢を求める
        /// これは制約の解決で利用される
        /// AnimationPoseRatioが1.0ならば不要なのでスキップされる
        /// </summary>
        [BurstCompile]
        struct UpdateStepBasicPotureJob : IJobParallelForDefer
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<int> stepBaseLineIndexArray;

            // team
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamManager.TeamData> teamDataArray;

            // vmesh
            [Unity.Collections.ReadOnly]
            public NativeArray<VertexAttribute> attributes;
            [Unity.Collections.ReadOnly]
            public NativeArray<int> vertexParentIndices;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> vertexLocalPositions;
            [Unity.Collections.ReadOnly]
            public NativeArray<quaternion> vertexLocalRotations;
            [Unity.Collections.ReadOnly]
            public NativeArray<ushort> baseLineStartDataIndices;
            [Unity.Collections.ReadOnly]
            public NativeArray<ushort> baseLineDataCounts;
            [Unity.Collections.ReadOnly]
            public NativeArray<ushort> baseLineData;

            // particle
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> basePosArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<quaternion> baseRotArray;

            // buffer
            [NativeDisableParallelForRestriction]
            public NativeArray<float3> stepBasicPositionArray;
            [NativeDisableParallelForRestriction]
            public NativeArray<quaternion> stepBasicRotationArray;

            // ステップ実行ベースラインごと
            public void Execute(int index)
            {
                // チームは有効であることが保証されている
                uint pack = (uint)stepBaseLineIndexArray[index];
                int teamId = DataUtility.Unpack32Hi(pack);
                int bindex = DataUtility.Unpack32Low(pack);

                var tdata = teamDataArray[teamId];

                // アニメーションポーズ使用の有無
                // 初期姿勢の計算が不要なら抜ける
                float blendRatio = tdata.animationPoseRatio;
                if (blendRatio > 0.99f)
                    return;

                int b_datastart = tdata.baseLineDataChunk.startIndex;
                int p_start = tdata.particleChunk.startIndex;
                int v_start = tdata.proxyCommonChunk.startIndex;

                // チームスケール
                float3 scl = tdata.initScale * tdata.scaleRatio;

                int b_start = baseLineStartDataIndices[bindex];
                int b_cnt = baseLineDataCounts[bindex];
                int b_dataindex = b_start + b_datastart;
                {
                    for (int i = 0; i < b_cnt; i++, b_dataindex++)
                    {
                        int l_index = baseLineData[b_dataindex];
                        int pindex = p_start + l_index;
                        int vindex = v_start + l_index;

                        // 親
                        int p_index = vertexParentIndices[vindex];
                        int p_pindex = p_index + p_start;

                        var attr = attributes[vindex];
                        if (attr.IsMove() && p_index >= 0)
                        {
                            // 移動
                            // 親から姿勢を算出する
                            var lpos = vertexLocalPositions[vindex];
                            var lrot = vertexLocalRotations[vindex];
                            var ppos = stepBasicPositionArray[p_pindex];
                            var prot = stepBasicRotationArray[p_pindex];

                            // マイナススケール
                            lpos *= tdata.negativeScaleDirection;
                            lrot = lrot.value * tdata.negativeScaleQuaternionValue;

                            stepBasicPositionArray[pindex] = math.mul(prot, lpos * scl) + ppos;
                            stepBasicRotationArray[pindex] = math.mul(prot, lrot);
                        }
                        else
                        {
                            // マイナススケール
                            var prot = stepBasicRotationArray[pindex];
                            var lw = float4x4.TRS(0, prot, tdata.negativeScaleDirection);
                            quaternion rot = MathUtility.ToRotation(lw.c1.xyz, lw.c2.xyz);
                            stepBasicRotationArray[pindex] = rot;
                        }
                    }
                }

                // アニメーション姿勢とブレンド
                if (blendRatio > Define.System.Epsilon)
                {
                    b_dataindex = b_start + b_datastart;
                    for (int i = 0; i < b_cnt; i++, b_dataindex++)
                    {
                        int l_index = baseLineData[b_dataindex];
                        int pindex = p_start + l_index;

                        var bpos = basePosArray[pindex];
                        var brot = baseRotArray[pindex];

                        stepBasicPositionArray[pindex] = math.lerp(stepBasicPositionArray[pindex], bpos, blendRatio);
                        stepBasicRotationArray[pindex] = math.slerp(stepBasicRotationArray[pindex], brot, blendRatio);
                    }
                }
            }
        }

        /// <summary>
        /// ステップ終了後の座標確定処理
        /// 步骤结束后坐标确定处理
        /// </summary>
        [BurstCompile]
        struct EndSimulationStepJob : IJobParallelForDefer
        {
            public float simulationDeltaTime;

            [Unity.Collections.ReadOnly]
            public NativeArray<int> stepParticleIndexArray;

            // team
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamManager.TeamData> teamDataArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<ClothParameters> parameterArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<InertiaConstraint.CenterData> centerDataArray;

            // vmesh
            [Unity.Collections.ReadOnly]
            public NativeArray<VertexAttribute> attributes;
            [Unity.Collections.ReadOnly]
            public NativeArray<float> vertexDepths;

            // particle
            [Unity.Collections.ReadOnly]
            public NativeArray<short> teamIdArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> nextPosArray;
            [NativeDisableParallelForRestriction]
            public NativeArray<float3> oldPosArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> velocityArray;
            [NativeDisableParallelForRestriction]
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> realVelocityArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> velocityPosArray;
            [NativeDisableParallelForRestriction]
            public NativeArray<float> frictionArray;
            [NativeDisableParallelForRestriction]
            public NativeArray<float> staticFrictionArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> collisionNormalArray;

            // ステップ有効パーティクルごと
            // 每有效步长粒子
            public void Execute(int index)
            {
                // パーティクルは有効であることが保証されている
                // 确保粒子有效
                int pindex = stepParticleIndexArray[index];
                int teamId = teamIdArray[pindex];
                var tdata = teamDataArray[teamId];
                var cdata = centerDataArray[teamId];
                var param = parameterArray[teamId];

                int pstart = tdata.particleChunk.startIndex;
                int l_index = pindex - pstart;

                // 各カテゴリのデータインデックス
                // 每个类别的数据索引
                int vindex = tdata.proxyCommonChunk.startIndex + l_index;

                var attr = attributes[vindex];
                var depth = vertexDepths[vindex];
                var nextPos = nextPosArray[pindex];
                var oldPos = oldPosArray[pindex];

                if (attr.IsMove() || tdata.IsSpring)
                {
                    // 移動パーティクル 移动粒子
                    var velocityOldPos = velocityPosArray[pindex];

#if true
                    // ■摩擦
                    float friction = frictionArray[pindex];
                    float3 cn = collisionNormalArray[pindex];
                    bool isCollision = math.lengthsq(cn) > Define.System.Epsilon; // 接触の有無
                    float staticFrictionParam = param.colliderCollisionConstraint.staticFriction * tdata.scaleRatio;
                    float dynamicFrictionParam = param.colliderCollisionConstraint.dynamicFriction;
#endif

#if true
                    // ■静止摩擦
                    float staticFriction = staticFrictionArray[pindex];
                    if (isCollision && friction > 0.0f && staticFrictionParam > 0.0f)
                    {
                        // 接線方向の移動速度から計算する
                        // 根据切线方向的移动速度计算
                        var v = nextPos - oldPos;
                        var tanv = v - MathUtility.Project(v, cn); // 接線方向の移動ベクトル 切线方向移动矢量
                        float tangentVelocity = math.length(tanv) / simulationDeltaTime; // 接線方向の移動速度 切线方向移动速度

                        // 静止速度以下ならば係数を上げる
                        if (tangentVelocity < staticFrictionParam)
                        {
                            staticFriction = math.saturate(staticFriction + 0.04f); // 係数増加(0.02?)
                        }
                        else
                        {
                            // 接線速度に応じて係数を減少
                            var vel = tangentVelocity - staticFrictionParam;
                            var value = math.max(vel / 0.2f, 0.05f);
                            staticFriction = math.saturate(staticFriction - value);
                        }

                        // 接線方向に位置を巻き戻す 沿切线方向回绕位置
                        tanv *= staticFriction;
                        nextPos -= tanv;
                        velocityOldPos -= tanv;
                    }
                    else
                    {
                        // 減衰
                        staticFriction = math.saturate(staticFriction - 0.05f);
                    }
                    staticFrictionArray[pindex] = staticFriction;
#endif

                    // ■速度更新(m/s) ------------------------------------------
                    // 速度計算用の位置から割り出す（制約ごとの速度調整用）
                    float3 velocity = (nextPos - velocityOldPos) / simulationDeltaTime;
                    float sqVel = math.lengthsq(velocity);
                    float3 normalVelocity = sqVel > Define.System.Epsilon ? math.normalize(velocity) : 0;

#if true
                    // ■動摩擦
                    // 衝突面との角度が大きいほど減衰が強くなる(MC1)
                    // ■动摩擦
                    // 与碰撞面的角度越大衰减越强（MC1）
                    if (friction > Define.System.Epsilon && isCollision && dynamicFrictionParam > 0.0f && sqVel >= Define.System.Epsilon)
                    {
                        //float dot = math.dot(cn, math.normalize(velocity));
                        float dot = math.dot(cn, normalVelocity);
                        dot = 0.5f + 0.5f * dot; // 1.0(front) - 0.5(side) - 0.0(back)
                        dot *= dot; // サイドを強めに
                        dot = 1.0f - dot; // 0.0(front) - 0.75(side) - 1.0(back)
                        velocity -= velocity * (dot * math.saturate(friction * dynamicFrictionParam));
                    }

                    // 摩擦減衰
                    friction *= Define.System.FrictionDampingRate;
                    frictionArray[pindex] = friction;
#endif

#if true
                    // 最大速度
                    // 最大速度はある程度制限したほうが動きが良くなるので入れるべき。
                    // 特に回転時の髪などの動きが柔らかくなる。
                    // しかし制限しすぎるとコライダーの押し出し制度がさがるので注意。
                    //最大速度
//最大速度在一定程度上限制的话动作会变好，所以应该放进去。
//特别是旋转时头发等的动作变软。
//但是过于限制的话，会降低合作组织的挤出制度，所以要注意。
                    if (param.inertiaConstraint.particleSpeedLimit >= 0.0f)
                    {
                        velocity = MathUtility.ClampVector(velocity, param.inertiaConstraint.particleSpeedLimit * tdata.scaleRatio);
                    }
#endif
#if true
                    // ■遠心力加速 ---------------------------------------------
                    // 离心力加速
                    if (cdata.angularVelocity > Define.System.Epsilon && param.inertiaConstraint.centrifualAcceleration > Define.System.Epsilon && sqVel >= Define.System.Epsilon)
                    {
                        // 回転中心のローカル座標
                        // 旋转中心局部坐标
                        var lpos = nextPos - cdata.nowWorldPosition;

                        // 回転軸平面に投影
                        // 投影到旋转轴平面
                        var v = MathUtility.ProjectOnPlane(lpos, cdata.rotationAxis);
                        var r = math.length(v);
                        if (r > Define.System.Epsilon)
                        {
                            float3 n = v / r;

                            // 角速度(rad/s)
                            float w = cdata.angularVelocity;

                            // 重量（重いほど遠心力は強くなる）
                            // ここでは末端に行くほど軽くする
                            // 重量（越重离心力越强）
                            // 此处越往末端越轻
                            //float m = (1.0f - depth) * 3.0f;
                            //float m = 1.0f + (1.0f - depth) * 2.0f;
                            float m = 1.0f + (1.0f - depth); // fix
                            //float m = 1.0f + depth * 3.0f;
                            //const float m = 1;

                            // 遠心力 离心力
                            var f = m * w * w * r;

                            // 回転方向uと速度方向が同じ場合のみ力を加える（内積による乗算）
                            // 実際の物理では遠心力は紐が張った状態でなければ発生しないがこの状態を判別する方法は簡単ではない
                            // そのためこのような近似で代用する
                            // 回転と速度が逆方向の場合は紐が緩んでいると判断し遠心力の増強を適用しない
                            // 旋转方向u仅在速度方向相同的情况下施加力（内积乘法）
                            // 在实际物理中，离心力如果不是拉紧绳子的状态就不会发生，但是判别该状态的方法并不简单
                            // 因此用这样的近似代替
                            // 旋转和速度为反方向时，判断为绳子松动，不适用离心力的增强
                            float3 u = math.normalize(math.cross(cdata.rotationAxis, n));
                            f *= math.saturate(math.dot(normalVelocity, u));

                            // 遠心力を速度に加算する 把离心力加到速度上
                            velocity += n * (f * param.inertiaConstraint.centrifualAcceleration * 0.02f);
                        }
                    }
#endif
                    // 安定化用の速度割合 稳定化用速度比例
                    velocity *= tdata.velocityWeight;

                    // 書き戻し 重写
                    velocityArray[pindex] = velocity;
                }

                // 実速度 实际速度
                float3 realVelocity = (nextPos - oldPos) / simulationDeltaTime;
                realVelocityArray[pindex] = realVelocity;
                //Debug.Log($"[{pindex}] realVelocity:{realVelocity}");

                // 今回の予測位置を記録
                // 记录这次的预测位置
                oldPosArray[pindex] = nextPos;
            }
        }

        //=========================================================================================
        /// <summary>
        /// シミュレーション完了後の表示位置の計算
        /// - 未来予測
        /// 模拟完成后的显示位置的计算
        /// 未来预测
        /// </summary>
        /// <param name="jobHandle"></param>
        /// <returns></returns>
        internal JobHandle CalcDisplayPosition(JobHandle jobHandle)
        {
            // ここではproxyMeshのpositionsのみを更新する
            // rotationsは自動で計算されるため
            // 在此proxyMesh的，之positions仅更新
            // rotations是自动计算的
            var job = new CalcDisplayPositionJob()
            {
                simulationDeltaTime = MagicaManager.Time.SimulationDeltaTime,

                teamDataArray = MagicaManager.Team.teamDataArray.GetNativeArray(),

                teamIdArray = teamIdArray.GetNativeArray(),
                oldPosArray = oldPosArray.GetNativeArray(),
                realVelocityArray = realVelocityArray.GetNativeArray(),
                oldPositionArray = oldPositionArray.GetNativeArray(),
                oldRotationArray = oldRotationArray.GetNativeArray(),
                dispPosArray = dispPosArray.GetNativeArray(),

                attributes = MagicaManager.VMesh.attributes.GetNativeArray(),
                positions = MagicaManager.VMesh.positions.GetNativeArray(),
                rotations = MagicaManager.VMesh.rotations.GetNativeArray(),
            };
            jobHandle = job.Schedule(ParticleCount, 32, jobHandle);

            return jobHandle;
        }

        [BurstCompile]
        struct CalcDisplayPositionJob : IJobParallelFor
        {
            public float simulationDeltaTime;

            // team
            [Unity.Collections.ReadOnly]
            public NativeArray<TeamManager.TeamData> teamDataArray;

            // particle
            [Unity.Collections.ReadOnly]
            public NativeArray<short> teamIdArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> oldPosArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<float3> realVelocityArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> oldPositionArray;
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> oldRotationArray;
            public NativeArray<float3> dispPosArray;

            // vmesh
            [Unity.Collections.ReadOnly]
            public NativeArray<VertexAttribute> attributes;
            [NativeDisableParallelForRestriction]
            public NativeArray<float3> positions;
            //[Unity.Collections.ReadOnly]
            [NativeDisableParallelForRestriction]
            public NativeArray<quaternion> rotations;

            // すべてのパーティクルごと
            public void Execute(int pindex)
            {
                int teamId = teamIdArray[pindex];
                if (teamId == 0)
                    return;

                var tdata = teamDataArray[teamId];
                if (tdata.IsProcess == false)
                    return;

                // ■この処理は更新に関係なく実行する

                int l_index = pindex - tdata.particleChunk.startIndex;
                int vindex = tdata.proxyCommonChunk.startIndex + l_index;

                var attr = attributes[vindex];

                var pos = positions[vindex];
                var rot = rotations[vindex];
                //Debug.Log($"DispRot [{vindex}] nor:{MathUtility.ToNormal(rot)}, tan:{MathUtility.ToTangent(rot)}");

                if (attr.IsMove() || tdata.IsSpring)
                {
                    // 移動パーティクル
                    var dpos = oldPosArray[pindex];

#if !MC2_DISABLE_FUTURE
                    // 未来予測
                    // 最終計算位置と実速度から次のステップ位置を予測し、その間のフレーム時間位置を表示位置とする
                    // 未来预测
                    // 根据最终计算位置和实际速度预测下一步位置，将其间帧时间位置作为显示位置
                    float3 velocity = realVelocityArray[pindex] * simulationDeltaTime;
                    float3 fpos = dpos + velocity;
                    float interval = (tdata.nowUpdateTime + simulationDeltaTime) - tdata.oldTime;
                    float t = interval > 0.0f ? (tdata.time - tdata.oldTime) / interval : 0.0f;
                    Debug.Log("lerp t " + t);
                    fpos = math.lerp(dispPosArray[pindex], fpos, t);
                    dpos = fpos;
#endif

                    // 表示位置
                    var dispPos = dpos;

                    // 表示位置を記録
                    dispPosArray[pindex] = dispPos;

                    // ブレンドウエイト
                    var vpos = math.lerp(positions[vindex], dispPos, tdata.blendWeight);

                    // vmeshに反映
                    positions[vindex] = vpos;
                }
                else
                {
                    // 固定パーティクル
                    // 表示位置は常にオリジナル位置
                    var dispPos = positions[vindex];
                    dispPosArray[pindex] = dispPos;
                }

                // １つ前の原点位置を記録
                // 记录前一个原点位置
                if (tdata.IsRunning)
                {
                    oldPositionArray[pindex] = pos;
                    oldRotationArray[pindex] = rot;
                }

                // マイナススケール
                // 回転をマイナススケールを適用した表示計算用の回転に変換する
                if (tdata.IsNegativeScale)
                {
                    var lw = float4x4.TRS(0, rot, tdata.negativeScaleDirection);
                    rot = MathUtility.ToRotation(lw.c1.xyz, lw.c2.xyz);
                    rotations[vindex] = rot;
                }
            }
        }

        //=========================================================================================
        /// <summary>
        /// tempFloat3Bufferの内容をnextPosArrayに書き戻す
        /// </summary>
        /// <param name="particleList"></param>
        /// <param name="jobHandle"></param>
        /// <returns></returns>
        internal JobHandle FeedbackTempFloat3Buffer(in NativeList<int> particleList, JobHandle jobHandle)
        {
            var job = new FeedbackTempPosJob()
            {
                jobParticleIndexList = particleList,
                tempFloat3Buffer = tempFloat3Buffer,
                nextPosArray = nextPosArray.GetNativeArray(),
            };
            jobHandle = job.Schedule(particleList, 32, jobHandle);

            return jobHandle;
        }

        [BurstCompile]
        struct FeedbackTempPosJob : IJobParallelForDefer
        {
            [Unity.Collections.ReadOnly]
            public NativeList<int> jobParticleIndexList;

            [Unity.Collections.ReadOnly]
            public NativeArray<float3> tempFloat3Buffer;

            [NativeDisableParallelForRestriction]
            public NativeArray<float3> nextPosArray;

            public void Execute(int index)
            {
                int pindex = jobParticleIndexList[index];
                nextPosArray[pindex] = tempFloat3Buffer[pindex];
            }
        }

        internal JobHandle FeedbackTempFloat3Buffer(in ExProcessingList<int> processingList, JobHandle jobHandle)
        {
            return FeedbackTempFloat3Buffer(processingList.Buffer, processingList.Counter, jobHandle);
        }

        unsafe internal JobHandle FeedbackTempFloat3Buffer(in NativeArray<int> particleArray, in NativeReference<int> counter, JobHandle jobHandle)
        {
            var job = new FeedbackTempPosJob2()
            {
                particleIndexArray = particleArray,
                tempFloat3Buffer = tempFloat3Buffer,
                nextPosArray = nextPosArray.GetNativeArray(),
            };
            jobHandle = job.Schedule((int*)counter.GetUnsafePtrWithoutChecks(), 32, jobHandle);

            return jobHandle;
        }

        [BurstCompile]
        struct FeedbackTempPosJob2 : IJobParallelForDefer
        {
            [Unity.Collections.ReadOnly]
            public NativeArray<int> particleIndexArray;

            [Unity.Collections.ReadOnly]
            public NativeArray<float3> tempFloat3Buffer;

            [NativeDisableParallelForRestriction]
            public NativeArray<float3> nextPosArray;

            public void Execute(int index)
            {
                int pindex = particleIndexArray[index];
                nextPosArray[pindex] = tempFloat3Buffer[pindex];
            }
        }

        //=========================================================================================
        public void InformationLog(StringBuilder allsb)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"========== Simulation Manager ==========");
            if (IsValid() == false)
            {
                sb.AppendLine($"Simulation Manager. Invalid");
            }
            else
            {
                sb.AppendLine($"Simulation Manager. Particle:{ParticleCount}");
                sb.AppendLine($"  -teamIdArray:{teamIdArray.ToSummary()}");
                sb.AppendLine($"  -nextPosArray:{nextPosArray.ToSummary()}");
                sb.AppendLine($"  -oldPosArray:{oldPosArray.ToSummary()}");
                sb.AppendLine($"  -oldRotArray:{oldRotArray.ToSummary()}");
                sb.AppendLine($"  -basePosArray:{basePosArray.ToSummary()}");
                sb.AppendLine($"  -baseRotArray:{baseRotArray.ToSummary()}");
                sb.AppendLine($"  -oldPositionArray:{oldPositionArray.ToSummary()}");
                sb.AppendLine($"  -oldRotationArray:{oldRotationArray.ToSummary()}");
                sb.AppendLine($"  -velocityPosArray:{velocityPosArray.ToSummary()}");
                sb.AppendLine($"  -dispPosArray:{dispPosArray.ToSummary()}");
                sb.AppendLine($"  -velocityArray:{velocityArray.ToSummary()}");
                sb.AppendLine($"  -realVelocityArray:{realVelocityArray.ToSummary()}");
                sb.AppendLine($"  -frictionArray:{frictionArray.ToSummary()}");
                sb.AppendLine($"  -staticFrictionArray:{staticFrictionArray.ToSummary()}");
                sb.AppendLine($"  -collisionNormalArray:{collisionNormalArray.ToSummary()}");

                // 制約
                sb.Append(distanceConstraint.ToString());
                sb.Append(bendingConstraint.ToString());
                sb.Append(angleConstraint.ToString());
                sb.Append(inertiaConstraint.ToString());
                sb.Append(colliderCollisionConstraint.ToString());
                sb.Append(selfCollisionConstraint.ToString());

                // 汎用バッファ
                sb.AppendLine($"[Step Buffer]");
                sb.AppendLine($"  -processingStepParticle:{processingStepParticle}");
                sb.AppendLine($"  -processingStepTriangleBending:{processingStepTriangleBending}");
                sb.AppendLine($"  -processingStepEdgeCollision:{processingStepEdgeCollision}");
                sb.AppendLine($"  -processingStepCollider:{processingStepCollider}");
                sb.AppendLine($"  -processingStepBaseLine:{processingStepBaseLine}");
                sb.AppendLine($"  -processingStepMotionParticle:{processingStepMotionParticle}");
                sb.AppendLine($"  -processingSelfParticle:{processingSelfParticle}");
                sb.AppendLine($"  -processingSelfPointTriangle:{processingSelfPointTriangle}");
                sb.AppendLine($"  -processingSelfEdgeEdge:{processingSelfEdgeEdge}");
                sb.AppendLine($"  -processingSelfTrianglePoint:{processingSelfTrianglePoint}");
                sb.AppendLine($"[Buffer]");
                sb.AppendLine($"  -tempFloat3Buffer:{(tempFloat3Buffer.IsCreated ? tempFloat3Buffer.Length : 0)}");
                sb.AppendLine($"  -countArray:{(countArray.IsCreated ? countArray.Length : 0)}");
                sb.AppendLine($"  -sumArray:{(sumArray.IsCreated ? sumArray.Length : 0)}");
                sb.AppendLine($"  -stepBasicPositionBuffer:{(stepBasicPositionBuffer.IsCreated ? stepBasicPositionBuffer.Length : 0)}");
                sb.AppendLine($"  -stepBasicRotationBuffer:{(stepBasicRotationBuffer.IsCreated ? stepBasicRotationBuffer.Length : 0)}");

                sb.AppendLine();
            }
            sb.AppendLine();
            Debug.Log(sb.ToString());
            allsb.Append(sb);
        }
    }
}
