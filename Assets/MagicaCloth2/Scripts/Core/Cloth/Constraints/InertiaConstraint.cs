// Magica Cloth 2.
// Copyright (c) 2023 MagicaSoft.
// https://magicasoft.jp
using System;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace MagicaCloth2
{
    /// <summary>
    /// Inertia and travel/rotation limits.
    /// 慣性と移動/回転制限
    /// </summary>
    public class InertiaConstraint : IDisposable
    {
        /// <summary>
        /// テレポートモード
        /// Teleport processing mode.
        /// </summary>
        public enum TeleportMode
        {
            None = 0,

            /// <summary>
            /// シミュレーションをリセットします
            /// Reset the simulation.
            /// </summary>
            Reset = 1,

            /// <summary>
            /// テレポート前の状態を継続します
            /// Continue the state before the teleport.
            /// </summary>
            Keep = 2,
        }

        [System.Serializable]
        public class SerializeData : IDataValidate
        {
            /// <summary>
            /// Anchor that cancels inertia.
            /// Anchor translation and rotation are excluded from simulation.
            /// This is useful if your character rides a vehicle.
            /// 慣性を打ち消すアンカー
            /// アンカーの移動と回転はシミュレーションから除外されます
            /// これはキャラクターが乗り物に乗る場合に便利です
            ///消除惯性的锚
            ///锚点移动和旋转将从模拟中排除
            ///这在角色乘坐交通工具时很方便
            /// [OK] Runtime changes.
            /// [NG] Export/Import with Presets
            /// </summary>
            public Transform anchor;

            /// <summary>
            /// Anchor Influence (0.0 ~ 1.0)
            /// アンカーの影響(0.0 ~ 1.0) 锚点影响（0.0-1.0）
            /// [OK] Runtime changes.
            /// [NG] Export/Import with Presets
            /// </summary>
            [Range(0.0f, 1.0f)]
            public float anchorInertia;


            /// <summary>
            /// World Influence (0.0 ~ 1.0).
            /// ワールド移動影響(0.0 ~ 1.0) 世界移动影响平滑化（0.0~1.0）
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            [FormerlySerializedAs("movementInertia")]
            [Range(0.0f, 1.0f)]
            public float worldInertia;

            /// <summary>
            /// World Influence Smoothing (0.0 ~ 1.0).
            /// ワールド移動影響平滑化(0.0 ~ 1.0)
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            [Range(0.0f, 1.0f)]
            public float movementInertiaSmoothing;

            /// <summary>
            /// World movement speed limit (m/s).
            /// ワールド移動速度制限(m/s) 世界移动速度限制
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public CheckSliderSerializeData movementSpeedLimit;

            /// <summary>
            /// World rotation speed limit (deg/s).
            /// ワールド回転速度制限(deg/s) 世界旋转速度限制
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public CheckSliderSerializeData rotationSpeedLimit;

            /// <summary>
            /// Local Influence (0.0 ~ 1.0).
            /// ローカル慣性影響(0.0 ~ 1.0) 局部惯性影响（0.0~1.0）
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            [Range(0.0f, 1.0f)]
            public float localInertia;

            /// <summary>
            /// Local movement speed limit (m/s).
            /// ローカル移動速度制限(m/s) 局部移动速度限制（m/s）
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public CheckSliderSerializeData localMovementSpeedLimit;

            /// <summary>
            /// Local rotation speed limit (deg/s).
            /// ローカル回転速度制限(deg/s) 局部旋转速度限制

            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public CheckSliderSerializeData localRotationSpeedLimit;

            /// <summary>
            /// depth inertia (0.0 ~ 1.0).
            /// Increasing the effect weakens the inertia near the root (makes it difficult to move).
            /// 深度慣性(0.0 ~ 1.0) 深度惯性（0.0-1.0）
            /// 影響を大きくするとルート付近の慣性が弱くなる（動きにくくなる） 增大影响会使路线附近的惯性变弱（难以移动）
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            [Range(0.0f, 1.0f)]
            public float depthInertia;

            /// <summary>
            /// Centrifugal acceleration (0.0 ~ 1.0).
            /// 遠心力加速(0.0 ~ 1.0) 离心力加速（0.0~1.0）
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            [Range(0.0f, 1.0f)]
            public float centrifualAcceleration;

            /// <summary>
            /// Particle Velocity Limit (m/s).
            /// パーティクル速度制限(m/s) 粒子速度限制
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public CheckSliderSerializeData particleSpeedLimit;

            /// <summary>
            /// Teleport determination method.
            /// テレポート判定モード 电信判定模式
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public TeleportMode teleportMode;

            /// <summary>
            /// Teleport detection distance.
            /// テレポート判定距離 传输判定距离
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public float teleportDistance;

            /// <summary>
            /// Teleport detection angle(deg).
            /// テレポート判定回転角度(deg) 传送判定旋转角度（deg）
            /// [OK] Runtime changes.
            /// [OK] Export/Import with Presets
            /// </summary>
            public float teleportRotation;

            public SerializeData()
            {
                anchor = null;
                anchorInertia = 0.0f;
                worldInertia = 1.0f;
                //movementInertiaSmoothing = 0.65f; // ->0.0524
                movementInertiaSmoothing = 0.5f; // ->0.13375
                movementSpeedLimit = new CheckSliderSerializeData(true, 5.0f);
                rotationSpeedLimit = new CheckSliderSerializeData(true, 720.0f);
                localInertia = 1.0f;
                localMovementSpeedLimit = new CheckSliderSerializeData(false, 5.0f);
                localRotationSpeedLimit = new CheckSliderSerializeData(false, 720.0f);
                depthInertia = 0.0f;
                centrifualAcceleration = 0.0f;
                particleSpeedLimit = new CheckSliderSerializeData(true, 4.0f);
                teleportMode = TeleportMode.None;
                teleportDistance = 0.5f;
                teleportRotation = 90.0f;
            }

            public SerializeData Clone()
            {
                return new SerializeData()
                {
                    anchor = anchor,
                    anchorInertia = anchorInertia,
                    worldInertia = worldInertia,
                    movementInertiaSmoothing = movementInertiaSmoothing,
                    movementSpeedLimit = movementSpeedLimit.Clone(),
                    rotationSpeedLimit = rotationSpeedLimit.Clone(),
                    localInertia = localInertia,
                    localMovementSpeedLimit = localMovementSpeedLimit.Clone(),
                    localRotationSpeedLimit = localRotationSpeedLimit.Clone(),
                    depthInertia = depthInertia,
                    centrifualAcceleration = centrifualAcceleration,
                    particleSpeedLimit = particleSpeedLimit.Clone(),
                    teleportMode = teleportMode,
                    teleportDistance = teleportDistance,
                    teleportRotation = teleportRotation,
                };
            }

            public void DataValidate()
            {
                anchorInertia = Mathf.Clamp01(anchorInertia);
                worldInertia = Mathf.Clamp01(worldInertia);
                movementInertiaSmoothing = Mathf.Clamp01(movementInertiaSmoothing);
                movementSpeedLimit.DataValidate(0.0f, Define.System.MaxMovementSpeedLimit);
                rotationSpeedLimit.DataValidate(0.0f, Define.System.MaxRotationSpeedLimit);
                localInertia = Mathf.Clamp01(localInertia);
                localMovementSpeedLimit.DataValidate(0.0f, Define.System.MaxMovementSpeedLimit);
                localRotationSpeedLimit.DataValidate(0.0f, Define.System.MaxRotationSpeedLimit);
                centrifualAcceleration = Mathf.Clamp01(centrifualAcceleration);
                depthInertia = Mathf.Clamp01(depthInertia);
                particleSpeedLimit.DataValidate(0.0f, Define.System.MaxParticleSpeedLimit);
                teleportDistance = Mathf.Max(teleportDistance, 0.0f);
                teleportRotation = Mathf.Max(teleportRotation, 0.0f);
            }
        }

        public struct InertiaConstraintParams
        {
            /// <summary>
            /// アンカー影響率(0.0 ~ 1.0)
            /// </summary>
            public float anchorInertia;

            /// <summary>
            /// ワールド慣性影響(0.0 ~ 1.0)
            /// </summary>
            public float worldInertia;

            /// <summary>
            /// ワールド慣性スムージング率(0.0 ~ 1.0)
            /// </summary>
            public float movementInertiaSmoothing;

            /// <summary>
            /// ワールド移動速度制限(m/s)
            /// 無制限時は(-1)
            /// </summary>
            public float movementSpeedLimit;

            /// <summary>
            /// ワールド回転速度制限(deg/s)
            /// 無制限時は(-1)
            /// </summary>
            public float rotationSpeedLimit;

            /// <summary>
            /// ローカル慣性影響(0.0 ~ 1.0)
            /// </summary>
            public float localInertia;

            /// <summary>
            /// ローカル移動速度制限(m/s)
            /// </summary>
            public float localMovementSpeedLimit;

            /// <summary>
            /// ローカル回転速度制限(deg/s)
            /// </summary>
            public float localRotationSpeedLimit;

            /// <summary>
            /// 深度慣性(0.0 ~ 1.0)
            /// 影響を大きくするとルート付近の慣性が弱くなる（動きにくくなる）
            /// </summary>
            public float depthInertia;

            /// <summary>
            /// 遠心力加速(0.0 ~ 1.0)
            /// </summary>
            public float centrifualAcceleration;

            /// <summary>
            /// パーティクル速度制限(m/s)
            /// </summary>
            public float particleSpeedLimit;

            /// <summary>
            /// テレポートモード
            /// </summary>
            public TeleportMode teleportMode;

            /// <summary>
            /// テレポート判定距離
            /// </summary>
            public float teleportDistance;

            /// <summary>
            /// テレポート判定角度(deg)
            /// </summary>
            public float teleportRotation;

            public void Convert(SerializeData sdata)
            {
                anchorInertia = sdata.anchorInertia;
                worldInertia = sdata.worldInertia;
                movementInertiaSmoothing = sdata.movementInertiaSmoothing;
                movementSpeedLimit = sdata.movementSpeedLimit.GetValue(-1);
                rotationSpeedLimit = sdata.rotationSpeedLimit.GetValue(-1);
                localInertia = sdata.localInertia;
                localMovementSpeedLimit = sdata.localMovementSpeedLimit.GetValue(-1);
                localRotationSpeedLimit = sdata.localRotationSpeedLimit.GetValue(-1);
                depthInertia = sdata.depthInertia;
                centrifualAcceleration = sdata.centrifualAcceleration;
                particleSpeedLimit = sdata.particleSpeedLimit.GetValue(-1);
                teleportMode = sdata.teleportMode;
                teleportDistance = sdata.teleportDistance;
                teleportRotation = sdata.teleportRotation;
            }
        }

        //=========================================================================================
        /// <summary>
        /// センタートランスフォームのデータ 中心变换数据
        /// </summary>
        [System.Serializable]
        public struct CenterData
        {
            /// <summary>
            /// 現在のアンカー姿勢 当前定位姿势
            /// </summary>
            public float3 anchorPosition;
            public quaternion anchorRotation;

            /// <summary>
            /// 前フレームのアンカー姿勢 前框架定位姿势
            /// </summary>
            public float3 oldAnchorPosition;
            public quaternion oldAnchorRotation;

            /// <summary>
            /// アンカー空間でのコンポーネントのローカル座標 元件在锚点空间中的局部坐标
            /// </summary>
            public float3 anchorComponentLocalPosition;

            /// <summary>
            /// 参照すべきセンタートランスフォームインデックス 要引用的中心变换索引
            /// 同期時は同期先チームのもにになる 同步时成为目标团队的成员
            /// </summary>
            public int centerTransformIndex;

            /// <summary>
            /// 現フレームのコンポーネント姿勢 当前帧的组件姿势
            /// </summary>
            public float3 componentWorldPosition;
            public quaternion componentWorldRotation;
            public float3 componentWorldScale;

            /// <summary>
            /// 前フレームのコンポーネント姿勢 前一帧的组件姿势
            /// </summary>
            public float3 oldComponentWorldPosition;
            public quaternion oldComponentWorldRotation;
            public float3 oldComponentWorldScale;

            /// <summary>
            /// 現フレームのコンポーネント移動量 当前框架的组件移动量(变化量)
            /// </summary>
            public float3 frameComponentShiftVector;
            public quaternion frameComponentShiftRotation;

            /// <summary>
            /// 現フレームのコンポーネント移動速度と方向 当前帧的组件移动速度和方向
            /// </summary>
            public float frameMovingSpeed;
            public float3 frameMovingDirection;

            /// <summary>
            /// 現フレームの姿勢 当前帧姿势（是固定点center数据）
            /// </summary>
            public float3 frameWorldPosition;
            public quaternion frameWorldRotation;
            public float3 frameWorldScale;
            public float3 frameLocalPosition;

            /// <summary>
            /// 前フレームの姿勢 前框架姿势
            /// </summary>
            public float3 oldFrameWorldPosition; // centerWorldPos(固定粒子中心)
            public quaternion oldFrameWorldRotation; //centerWorldRot（固定粒子旋转中心）
            public float3 oldFrameWorldScale;

            /// <summary>
            /// 現ステップでの姿勢 当前步骤中的姿势
            /// </summary>
            public float3 nowWorldPosition;
            public quaternion nowWorldRotation;
            //public float3 nowWorldScale; // ※現在未使用

            /// <summary>
            /// 前回ステップでの姿勢 上一步的姿势
            /// </summary>
            public float3 oldWorldPosition;
            public quaternion oldWorldRotation;

            /// <summary>
            /// ステップごとの移動力削減割合(0.0~1.0)
            /// 每步移动力削减比例（0.0~1.0）
            /// </summary>
            public float stepMoveInertiaRatio;

            /// <summary>
            /// ステップごとの回転力削減割合(0.0~1.0)
            /// 每步旋转力削减比例（0.0~1.0）
            /// </summary>
            public float stepRotationInertiaRatio;

            /// <summary>
            /// ステップごとの移動ベクトル 每步移动向量
            /// これは削減前の純粋なワールドベクトル 这是减少前的纯世界向量
            /// </summary>
            public float3 stepVector;

            /// <summary>
            /// ステップごとの回転ベクトル 每步旋转向量
            /// これは削減前の純粋なワールド回転 这是减少前的纯世界旋转
            /// </summary>
            public quaternion stepRotation;

            /// <summary>
            /// ステップごとの慣性全体移動シフトベクトル
            /// 每步惯性整体移动位移矢量
            /// </summary>
            public float3 inertiaVector;

            /// <summary>
            /// ステップごとの慣性全体シフト回転
            /// 每步惯性整体位移旋转
            /// </summary>
            public quaternion inertiaRotation;

            /// <summary>
            /// ステップごとの慣性削減後の移動速度(m/s)
            /// 每步惯性降低后的移动速度（m/s）
            /// </summary>
            public float stepMovingSpeed;

            /// <summary>
            /// ステップごとの慣性削減後の移動方向
            /// 每一步惯性削减后的移动方向
            /// </summary>
            public float3 stepMovingDirection;

            /// <summary>
            /// 回転の角速度(rad/s)
            /// 旋转角速度
            /// </summary>
            public float angularVelocity;

            /// <summary>
            /// 回転軸(角速度0の場合は(0,0,0))
            /// 旋转轴（角速度为0时为（0,0,0））
            /// </summary>
            public float3 rotationAxis;

            /// <summary>
            /// 初期化時の慣性中心姿勢でのローカル重力方向 初始化时惯性中心姿势下的局部重力方向
            /// 重力falloff計算で使用 用于重力模糊计算
            /// </summary>
            public float3 initLocalGravityDirection;

            /// <summary>
            /// スムージングされた現在のワールド慣性速度ベクトル
            /// 平滑的当前世界惯性速度矢量
            /// </summary>
            public float3 smoothingVelocity; // (m/s)

            /// <summary>
            /// マイナススケールによる反転を打ち消すための変換マトリックス 用于消除负比例反转的变换矩阵
            /// センター空間 中心空间
            /// </summary>
            public float4x4 negativeScaleMatrix;

            internal void Initialize()
            {
                anchorRotation = quaternion.identity;
                oldAnchorRotation = quaternion.identity;

                componentWorldRotation = quaternion.identity;
                componentWorldScale = 1;
                oldComponentWorldRotation = quaternion.identity;
                oldComponentWorldScale = 1;
                frameComponentShiftRotation = quaternion.identity;

                frameWorldRotation = quaternion.identity;
                oldFrameWorldRotation = quaternion.identity;
                nowWorldRotation = quaternion.identity;
                oldWorldRotation = quaternion.identity;
                stepRotation = quaternion.identity;
            }
        }

        /// <summary>
        /// 制約データ
        /// </summary>
        [System.Serializable]
        public class ConstraintData
        {
            public ResultCode result;
            public CenterData centerData;
            public float3 initLocalGravityDirection;
        }

        /// <summary>
        /// チームごとの固定点リスト
        /// </summary>
        internal ExNativeArray<ushort> fixedArray;

        //=========================================================================================
        public InertiaConstraint()
        {
            fixedArray = new ExNativeArray<ushort>(0, true);
        }

        public void Dispose()
        {
            fixedArray?.Dispose();
            fixedArray = null;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"[InertiaConstraint]");
            sb.AppendLine($"  -fixedArray:{fixedArray.ToSummary()}");

            return sb.ToString();
        }

        //=========================================================================================
        /// <summary>
        /// 制約データの作成
        /// 创建约束数据
        /// </summary>
        /// <param name="cbase"></param>
        public static ConstraintData CreateData(VirtualMesh proxyMesh, in ClothParameters parameters)
        {
            var constraintData = new ConstraintData();

            try
            {
                // ■センター 中锋
                var cdata = new CenterData();
                cdata.Initialize();
                cdata.centerTransformIndex = proxyMesh.centerTransformIndex;
                constraintData.centerData = cdata;

                // 固定点リストはすでにproxyMeshのcenterFixedListに格納されている
                // 固定点列表已存储在proxyMesh的centerFixedList中
                float3 nor = 0;
                float3 tan = 0;
                int ccnt = proxyMesh.CenterFixedPointCount;
                if (ccnt > 0)
                {
                    for (int i = 0; i < ccnt; i++)
                    {
                        int fixedIndex = proxyMesh.centerFixedList[i];

                        // 初期姿勢を求める 求初始姿势
                        var lnor = proxyMesh.localNormals[fixedIndex];
                        var ltan = proxyMesh.localTangents[fixedIndex];
                        var lrot = MathUtility.ToRotation(lnor, ltan);
                        var bindRot = proxyMesh.vertexBindPoseRotations[fixedIndex];
                        var q = math.mul(lrot, bindRot);
                        nor += MathUtility.ToNormal(q);
                        tan += MathUtility.ToTangent(q);
                    }
                }

                float3 localGravityDirection = new float3(0, -1, 0);
                if (ccnt > 0)
                {
                    // 初期センター姿勢からローカル重力方向を算出する
                    // 根据初始中心姿势计算局部重力方向
                    var rot = MathUtility.ToRotation(math.normalize(nor), math.normalize(tan));
                    var irot = math.inverse(rot);
                    localGravityDirection = math.mul(irot, parameters.worldGravityDirection);
                }
                constraintData.initLocalGravityDirection = localGravityDirection;

                constraintData.result.SetSuccess();

                //Develop.Log($"Create [InertiaConstraint].");
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                constraintData.result.SetError(Define.Result.Constraint_CreateInertiaException);
                throw;
            }
            finally
            {
            }

            return constraintData;
        }

        internal void Register(ClothProcess cprocess)
        {
            // センターデータのセンタートランスフォームインデックスをダイレクト値に変更
            // 将中心数据的中心变换索引更改为直接值
            ref var tdata = ref MagicaManager.Team.GetTeamDataRef(cprocess.TeamId);
            ref var cdata = ref MagicaManager.Team.centerDataArray.GetRef(cprocess.TeamId);
            cdata.centerTransformIndex = tdata.centerTransformIndex;

            // 初期化時のローカル重力方向
            cdata.initLocalGravityDirection = cprocess.inertiaConstraintData.initLocalGravityDirection;
            //Debug.Log($"[{cprocess.TeamId}] initLocalGravityDirection:{cdata.initLocalGravityDirection}");

            // 固定点リスト
            var c = new DataChunk();
            if (cprocess.ProxyMeshContainer.shareVirtualMesh.CenterFixedPointCount > 0)
            {
                c = fixedArray.AddRange(cprocess.ProxyMeshContainer.shareVirtualMesh.centerFixedList);
            }
            tdata.fixedDataChunk = c;
        }

        internal void Exit(ClothProcess cprocess)
        {
            if (cprocess != null && cprocess.TeamId > 0)
            {
                ref var tdata = ref MagicaManager.Team.GetTeamDataRef(cprocess.TeamId);

                fixedArray.Remove(tdata.fixedDataChunk);
                tdata.fixedDataChunk.Clear();
            }
        }
    }
}
