// Magica Cloth 2.
// Copyright (c) 2023 MagicaSoft.
// https://magicasoft.jp
using System;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;

namespace MagicaCloth2
{
    /// <summary>
    /// MagicaClothで利用する仮想メッシュ構造
    /// ・メッシュの親子関係により頂点が連動する
    /// ・トライアングルだけでなくライン構造も認める
    /// ・スレッドで利用できるようにする
    /// ・頂点数は最大65535に制限、ただし一部を除きインデックスはintで扱う
    /// </summary>
    public partial class VirtualMesh : IDisposable, IValid
    {

        public string name = string.Empty;

        public ResultCode result = new ResultCode();

        public bool isManaged; // PreBuild DeserializeManager管理

        /// <summary>
        /// メッシュタイプ
        /// </summary>
        public enum MeshType
        {
            /// <summary>
            /// 通常のメッシュ
            /// 座標計算：Transformからスキニング、法線接線計算：Transformからスキニング
            /// </summary>
            NormalMesh = 0,

            /// <summary>
            /// Transform連動メッシュ
            /// 座標計算：Transform直値、法線接線計算：Transform直値
            /// </summary>
            NormalBoneMesh = 1,

            /// <summary>
            /// プロキシメッシュ
            /// 法線接線の計算を接続するトライアングルから求める
            /// 座標計算：Transformからスキニング、法線接線計算：接続トライアングル平均化 or スキニング直値
            /// </summary>
            ProxyMesh = 2,

            /// <summary>
            /// Transform連動プロキシメッシュ
            /// シミュレーション前にTransformの復元を行い、シミュレーション後にTransformを書き込む
            /// 座標計算：Transform直値、法線接線計算：接続トライアングル平均化 or Transform直値
            /// </summary>
            ProxyBoneMesh = 3,

            /// <summary>
            /// マッピングメッシュ(MeshClothのみ)
            /// メッシュがプロキシメッシュにマッピングされた状態
            /// 座標計算：接続ProxyMesh頂点からスキニング、法線接線計算：接続ProxyMeshからスキニング
            /// </summary>
            Mapping = 4,
        }
        public MeshType meshType = MeshType.NormalMesh;

        /// <summary>
        /// Transformと直接連動するかどうか(=BoneCloth)
        /// </summary>
        public bool isBoneCloth = false;

        //=========================================================================================
        // メッシュ情報（基本）
        //=========================================================================================
        /// <summary>
        /// 現在の頂点が指す元の頂点インデックス
        /// </summary>
        public ExSimpleNativeArray<int> referenceIndices = new ExSimpleNativeArray<int>();

        /// <summary>
        /// 頂点属性
        /// </summary>
        public ExSimpleNativeArray<VertexAttribute> attributes = new ExSimpleNativeArray<VertexAttribute>();

        public ExSimpleNativeArray<float3> localPositions = new ExSimpleNativeArray<float3>();
        public ExSimpleNativeArray<float3> localNormals = new ExSimpleNativeArray<float3>();
        public ExSimpleNativeArray<float3> localTangents = new ExSimpleNativeArray<float3>();

        /// <summary>
        /// VirtualMeshのUVはTangent計算用でありテクスチャマッピング用ではないので注意！
        /// </summary>
        public ExSimpleNativeArray<float2> uv = new ExSimpleNativeArray<float2>();
        public ExSimpleNativeArray<VirtualMeshBoneWeight> boneWeights = new ExSimpleNativeArray<VirtualMeshBoneWeight>();

        // 每个元素是三角形的三个顶点索引
        public ExSimpleNativeArray<int3> triangles = new ExSimpleNativeArray<int3>();
        // 每个粒子之间连接的线/边，每个元素是线
        public ExSimpleNativeArray<int2> lines = new ExSimpleNativeArray<int2>();

        /// <summary>
        /// メッシュの基準トランスフォーム
        /// </summary>
        public int centerTransformIndex = -1;

        /// <summary>
        /// このメッシュの基準マトリックスと回転
        /// 此网格的基准矩阵和旋转
        /// </summary>
        public float4x4 initLocalToWorld;
        public float4x4 initWorldToLocal;
        public quaternion initRotation;
        public quaternion initInverseRotation;
        public float3 initScale;

        /// <summary>
        /// 計算用スケール(X軸のみで判定)
        /// 计算用标尺（仅用X轴判定）
        /// </summary>
        public float InitCalcScale => initScale.x;

        /// <summary>
        /// スキニングルートボーン
        /// 蒙皮骨骼
        /// </summary>
        public int skinRootIndex = -1;

        /// <summary>
        /// スキニングボーン
        /// 蒙皮骨骼
        /// </summary>
        public ExSimpleNativeArray<int> skinBoneTransformIndices = new ExSimpleNativeArray<int>();
        public ExSimpleNativeArray<float4x4> skinBoneBindPoses = new ExSimpleNativeArray<float4x4>();

        /// <summary>
        /// このメッシュで利用するTransformの情報
        /// 用于此网格的变换信息
        /// </summary>
        public TransformData transformData;

        /// <summary>
        /// バウンディングボックス
        /// 边界框
        /// </summary>
        public NativeReference<AABB> boundingBox;

        /// <summary>
        /// 頂点の平均接続距離
        /// 平均顶点连接距离
        /// </summary>
        public NativeReference<float> averageVertexDistance;

        /// <summary>
        /// 頂点の最大接続距離
        /// 顶点的最大连接距离
        /// </summary>
        public NativeReference<float> maxVertexDistance;

        public bool IsSuccess => result.IsSuccess();
        public bool IsError => result.IsError();
        public bool IsProcess => result.IsProcess();
        public int VertexCount => localPositions.Count;
        public int TriangleCount => triangles.Count;
        public int LineCount => lines.Count;
        public int SkinBoneCount => skinBoneTransformIndices.Count;
        public int TransformCount => transformData?.Count ?? 0;
        public bool IsProxy => meshType == MeshType.ProxyMesh || meshType == MeshType.ProxyBoneMesh;
        public bool IsMapping => meshType == MeshType.Mapping;

        //=========================================================================================
        // 結合／リダクション
        //=========================================================================================
        /// <summary>
        /// 結合された頂点範囲（結合もとメッシュが保持する）
        /// </summary>
        public DataChunk mergeChunk;

        /// <summary>
        /// 元の頂点からの結合とリダクション後の頂点へのインデックス
        /// </summary>
        public NativeArray<int> joinIndices;

        //=========================================================================================
        // プロキシメッシュ
        //=========================================================================================
        /// <summary>
        /// 頂点ごとの接続トライアングルインデックスと法線接線フリップフラグ（最大７つ）
        /// これは法線を再計算するために用いられるもので７つあれば十分であると判断したもの。
        /// そのため正確なトライアングル接続を表していない。
        /// データは12-20bitのuintでパックされている
        /// 12(hi) = 法線接線のフリップフラグ(法線:0x1,接線:0x2)。ONの場合フリップ。
        /// 20(low) = トライアングルインデックス。
        /// 每个顶点的连接三角索引和法线切线翻转标志（最多7个）
        /// 这是为了重新计算法线而使用的东西，如果有7个就足够了。
        /// 因此没有表示正确的三角连接。
        /// 数据以12-20bit的uint进行包装
        /// 12（hi）=法线切线的翻转标志（法线：0x1，切线：0x2）。ON时翻转。
        ///20（low）=三角索引。
        /// </summary>
        public NativeArray<FixedList32Bytes<uint>> vertexToTriangles;

        /// <summary>
        /// 頂点ごとの接続頂点インデックス
        /// vertexToVertexIndexArrayは頂点ごとのデータ開始インデックス(22bit)とカウンタ(10bit)を１つのuintに結合したもの
        ///每个顶点的连接顶点索引
        /// vertexToVertexIndexArray是将每个顶点的数据开始索引（22bit）和计数器（10bit）结合到一个uint上而成的
        /// todo:ここはMultiHashMapのままで良いかもしれない
        /// </summary>
        public NativeArray<uint> vertexToVertexIndexArray;
        public NativeArray<ushort> vertexToVertexDataArray;

        /// <summary>
        /// エッジリスト
        /// 边列表（所有连接的边）
        /// </summary>
        public NativeArray<int2> edges;

        /// <summary>
        /// エッジ固有フラグ
        /// 特定边标志
        /// </summary>
        public const byte EdgeFlag_Cut = 0x1; // 切り口エッジ
        public NativeArray<ExBitFlag8> edgeFlags;

        /// <summary>
        /// エッジごとの接続トライアングルインデックス
        /// 每个边的连接三角索引
        /// </summary>
        public NativeParallelMultiHashMap<int2, ushort> edgeToTriangles;

        /// <summary>
        /// 頂点ごとのバインドポーズ
        /// 頂点バインドにはスケール値は不要
        /// 每个顶点的绑定姿势
        /// 顶点绑定不需要缩放值
        /// </summary>
        public NativeArray<float3> vertexBindPosePositions;
        public NativeArray<quaternion> vertexBindPoseRotations;

        /// <summary>
        /// 頂点ごとの計算姿勢からTransformへの変換回転(BoneClothのみ)
        /// 将每个顶点的计算姿态转换为变换旋转（仅BoneCloth）
        /// </summary>
        public NativeArray<quaternion> vertexToTransformRotations;

        /// <summary>
        /// 各頂点のレベル（起点は０から）
        /// 每个顶点的级别（起点为0）
        /// </summary>
        //public NativeArray<byte> vertexLevels;

        /// <summary>
        /// 各頂点の深さ(0.0-1.0)
        /// </summary>
        public NativeArray<float> vertexDepths;

        /// <summary>
        /// 各頂点のルートインデックス(-1=なし)
        /// </summary>
        public NativeArray<int> vertexRootIndices;

        /// <summary>
        /// 各頂点の親頂点インデックス(-1=なし)
        /// </summary>
        public NativeArray<int> vertexParentIndices;

        /// <summary>
        /// 各頂点の子頂点インデックスリスト
        /// </summary>
        public NativeArray<uint> vertexChildIndexArray;
        public NativeArray<ushort> vertexChildDataArray;

        /// <summary>
        /// 各頂点の親からの基準ローカル座標
        /// </summary>
        public NativeArray<float3> vertexLocalPositions;

        /// <summary>
        /// 各頂点の親からの基準ローカル回転
        /// </summary>
        public NativeArray<quaternion> vertexLocalRotations;

        /// <summary>
        /// 法線調整用回転
        /// </summary>
        public NativeArray<quaternion> normalAdjustmentRotations;

#if false
        /// <summary>
        /// 角度計算用基準法線の計算方法
        /// </summary>
        public enum NormalCalcMode
        {
            Auto = 0,
            X_Axis = 1,
            Y_Axis = 2,
            Z_Axis = 3,
            Point_Outside = 4,
            Point_Inside = 5,
            //Line_Outside = 6,
            //Line_Inside = 7,
        }
        //public NormalCalcMode normalCalcMode = NormalCalcMode.Auto;

        /// <summary>
        /// 各頂点の角度計算用基準ローカル回転
        /// -AngleLimitConstraintで使用する
        /// pitch/yaw個別制限はv1.0では実装しないので一旦停止させる
        /// </summary>
        //public NativeArray<quaternion> vertexAngleCalcLocalRotations;
#endif

        /// <summary>
        /// 最大レベル値
        /// </summary>
        //public NativeReference<int> vertexMaxLevel;

        /// <summary>
        /// ベースラインごとのフラグ
        /// 每个基线的标志
        /// </summary>
        public const byte BaseLineFlag_IncludeLine = 0x01; // ラインを含む
        public NativeArray<ExBitFlag8> baseLineFlags;

        /// <summary>
        /// ベースラインごとのデータ開始インデックス
        /// 每个基线的数据起始索引
        /// </summary>
        public NativeArray<ushort> baseLineStartDataIndices;

        /// <summary>
        /// ベースラインごとのデータ数
        /// 每个基线的数据数
        /// </summary>
        public NativeArray<ushort> baseLineDataCounts;

        /// <summary>
        /// ベースラインデータ（頂点インデックス）
        /// 基线数据（顶点索引）
        /// </summary>
        public NativeArray<ushort> baseLineData;

        /// <summary>
        /// カスタムスキニングボーンの登録トランスフォームインデックス
        /// 自定义蒙皮骨骼注册变换索引
        /// </summary>
        public int[] customSkinningBoneIndices;

        /// <summary>
        /// センター計算用の固定頂点リスト
        /// 中心计算的固定顶点列表
        /// </summary>
        public ushort[] centerFixedList;

        /// <summary>
        /// プロキシメッシュ構築時のセンター位置（ローカル） 构建代理网格时的中心位置（本地）
        /// 固定頂点が無い場合は(0,0,0) 没有固定顶点时（0,0,0）
        /// </summary>
        public NativeReference<float3> localCenterPosition;

        public int BaseLineCount => baseLineStartDataIndices.IsCreated ? baseLineStartDataIndices.Length : 0;
        public int EdgeCount => edges.IsCreated ? edges.Length : 0;
        public int CustomSkinningBoneCount => customSkinningBoneIndices?.Length ?? 0;
        public int CenterFixedPointCount => centerFixedList?.Length ?? 0;
        public int NormalAdjustmentRotationCount => normalAdjustmentRotations.IsCreated ? normalAdjustmentRotations.Length : 0;

        //=========================================================================================
        // マッピングメッシュ 映射网格
        //=========================================================================================
        /// <summary>
        /// 接続しているプロキシメッシュ 连接的代理网格
        /// </summary>
        public VirtualMesh mappingProxyMesh;

        /// <summary>
        /// マッピングメッシュのセンタートランスフォーム情報 映射网格的中心变换信息
        /// 毎フレーム更新される 每帧更新
        /// Meshの場合はレンダラーのトランスフォームとなる 在网格的情况下是渲染器的变换
        /// </summary>
        public float3 centerWorldPosition;
        public quaternion centerWorldRotation;
        public float3 centerWorldScale;

        /// <summary>
        /// 初期状態でのマッピングメッシュへの変換マトリックスと変換回転 初始状态下到映射网格的转换矩阵和转换旋转
        /// この姿勢は初期化時に固定される 该姿势在初始化时被固定
        /// </summary>
        public float4x4 toProxyMatrix;
        public quaternion toProxyRotation;

        /// <summary>
        /// マネージャへの登録ID 管理器注册标识
        /// </summary>
        public int mappingId;

        //=========================================================================================
        public VirtualMesh() { }

        public VirtualMesh(bool initialize)
        {
            if (initialize)
            {
                transformData = new TransformData(100);

                // 最小限のデータ 最小数据
                averageVertexDistance = new NativeReference<float>(0.0f, Allocator.Persistent);
                maxVertexDistance = new NativeReference<float>(0.0f, Allocator.Persistent);

                // 作業中にしておく 在作业中放置
                result.SetProcess();
            }
        }

        public VirtualMesh(string name) : this(true)
        {
            this.name = name;
        }

        public void Dispose()
        {
            // PreBuild DeserializeManager管理中は破棄させない
            if (isManaged)
                return;

            result.Clear();
            referenceIndices.Dispose();
            attributes.Dispose();
            localPositions.Dispose();
            localNormals.Dispose();
            localTangents.Dispose();
            uv.Dispose();
            boneWeights.Dispose();
            triangles.Dispose();
            lines.Dispose();
            skinBoneTransformIndices.Dispose();
            skinBoneBindPoses.Dispose();

            if (joinIndices.IsCreated)
                joinIndices.Dispose();

            if (vertexToTriangles.IsCreated)
                vertexToTriangles.Dispose();
            if (vertexToVertexIndexArray.IsCreated)
                vertexToVertexIndexArray.Dispose();
            if (vertexToVertexDataArray.IsCreated)
                vertexToVertexDataArray.Dispose();
            if (edges.IsCreated)
                edges.Dispose();
            if (edgeFlags.IsCreated)
                edgeFlags.Dispose();
            if (edgeToTriangles.IsCreated)
                edgeToTriangles.Dispose();
            if (vertexBindPosePositions.IsCreated)
                vertexBindPosePositions.Dispose();
            if (vertexBindPoseRotations.IsCreated)
                vertexBindPoseRotations.Dispose();
            if (vertexToTransformRotations.IsCreated)
                vertexToTransformRotations.Dispose();
            if (vertexRootIndices.IsCreated)
                vertexRootIndices.Dispose();
            if (vertexParentIndices.IsCreated)
                vertexParentIndices.Dispose();
            if (vertexChildIndexArray.IsCreated)
                vertexChildIndexArray.Dispose();
            if (vertexChildDataArray.IsCreated)
                vertexChildDataArray.Dispose();
            if (baseLineFlags.IsCreated)
                baseLineFlags.Dispose();
            if (baseLineStartDataIndices.IsCreated)
                baseLineStartDataIndices.Dispose();
            if (baseLineDataCounts.IsCreated)
                baseLineDataCounts.Dispose();
            if (baseLineData.IsCreated)
                baseLineData.Dispose();
            if (localCenterPosition.IsCreated)
                localCenterPosition.Dispose();
            if (vertexLocalPositions.IsCreated)
                vertexLocalPositions.Dispose();
            if (vertexLocalRotations.IsCreated)
                vertexLocalRotations.Dispose();
            if (normalAdjustmentRotations.IsCreated)
                normalAdjustmentRotations.Dispose();
            //if (vertexAngleCalcLocalRotations.IsCreated)
            //    vertexAngleCalcLocalRotations.Dispose();
            //if (vertexMaxLevel.IsCreated)
            //    vertexMaxLevel.Dispose();
            //if (vertexLevels.IsCreated)
            //    vertexLevels.Dispose();
            if (vertexDepths.IsCreated)
                vertexDepths.Dispose();
            if (boundingBox.IsCreated)
                boundingBox.Dispose();
            if (averageVertexDistance.IsCreated)
                averageVertexDistance.Dispose();
            if (maxVertexDistance.IsCreated)
                maxVertexDistance.Dispose();

            transformData?.Dispose();
        }

        public void SetName(string newName)
        {
            name = newName;
        }

        /// <summary>
        /// 最低限のデータ検証
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            if (transformData == null)
                return false;

            // レンダラーが存在する場合はその存在を確認する
            // ただしPreBuildではtransformListは空なのでスキップする
            if (centerTransformIndex >= 0 && transformData.IsEmpty == false && transformData.GetTransformFromIndex(centerTransformIndex) == null)
                return false;

            return true;
        }

        //=========================================================================================
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"===== {name} =====");
            sb.AppendLine($"Result:{result.GetResultString()}");
            sb.AppendLine($"Type:{meshType}");
            sb.AppendLine($"Vertex:{VertexCount}");
            sb.AppendLine($"Line:{LineCount}");
            sb.AppendLine($"Triangle:{TriangleCount}");
            sb.AppendLine($"Edge:{EdgeCount}");
            sb.AppendLine($"SkinBone:{SkinBoneCount}");
            sb.AppendLine($"Transform:{TransformCount}");
            if (averageVertexDistance.IsCreated)
                sb.AppendLine($"avgDist:{averageVertexDistance.Value}");
            if (maxVertexDistance.IsCreated)
                sb.AppendLine($"maxDist:{maxVertexDistance.Value}");
            if (boundingBox.IsCreated)
                sb.AppendLine($"AABB:{boundingBox.Value}");
            sb.AppendLine();

            sb.AppendLine($"<<< Proxy >>>");
            sb.AppendLine($"BaseLine:{BaseLineCount}");
            sb.AppendLine($"EdgeCount:{EdgeCount}");
            int edgeToTrianglesCnt = edgeToTriangles.IsCreated ? edgeToTriangles.Count() : 0;
            sb.AppendLine($"edgeToTriangles:{edgeToTrianglesCnt}");
            sb.AppendLine($"CustomSkinningBoneCount:{CustomSkinningBoneCount}");
            sb.AppendLine($"CenterFixedPointCount:{CenterFixedPointCount}");
            sb.AppendLine($"NormalAdjustmentRotationCount:{NormalAdjustmentRotationCount}");
            sb.AppendLine();

            sb.AppendLine($"<<< Mapping >>>");
            sb.AppendLine($"centerWorldPosition:{centerWorldPosition}");
            sb.AppendLine();

            //sb.AppendLine($"<<< TransformData >>>");
            sb.AppendLine(transformData?.ToString() ?? "(none)");
            sb.AppendLine();

            return sb.ToString();
        }
    }
}
