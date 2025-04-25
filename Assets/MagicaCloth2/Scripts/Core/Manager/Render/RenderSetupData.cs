// Magica Cloth 2.
// Copyright (c) 2023 MagicaSoft.
// https://magicasoft.jp
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace MagicaCloth2
{
    /// <summary>
    /// 描画対象の基本情報 绘图对象的基本信息
    /// レンダラーまたはボーン構成情報 渲染器或骨骼配置信息
    /// この情報をもとに仮想メッシュなどを作成する 根据该信息创建虚拟网格等
    /// またこの情報はキャラクターが動き出す前に取得しておく必要がある 另外，这个信息需要在角色开始移动之前取得
    /// そのためAwake()などで実行する 因此在Awake（）等中执行
    /// </summary>
    public partial class RenderSetupData : IDisposable, ITransform
    {
        public ResultCode result;
        public string name = string.Empty;
        public bool isManaged; // pre-build DeserializeManager管理

        // タイプ
        public enum SetupType
        {
            MeshCloth = 0,
            BoneCloth = 1,
            BoneSpring = 2,
        }
        public SetupType setupType;

        // Mesh ---------------------------------------------------------------
        // レンダラーとメッシュ情報
        public Renderer renderer;
        public SkinnedMeshRenderer skinRenderer;
        public MeshFilter meshFilter;
        public Mesh originalMesh;
        public int vertexCount;
        public bool hasSkinnedMesh; // SkinnedMeshRendererを利用しているかどうか  是否利用
        public bool hasBoneWeight; // SkinnedMeshRendererでもボーンウエイトを持っていないケースあり！ 但是有没有骨骼重量的情况！
        public Mesh.MeshDataArray meshDataArray;　// Jobで利用するためのMeshData
        public int skinRootBoneIndex;
        public int skinBoneCount;

        // MeshDataでは取得できないメッシュ情報 在MeshData中无法获取的网格信息
        public List<Matrix4x4> bindPoseList;
        public NativeArray<byte> bonesPerVertexArray;
        public NativeArray<BoneWeight1> boneWeightArray;

        // PreBuild時のみ保持する情報.逆にmeshDataArrayは持たない 只保留时间信息。相反，没有meshDataArray
        public NativeArray<Vector3> localPositions;
        public NativeArray<Vector3> localNormals;

        // Bone ---------------------------------------------------------------
        public List<int> rootTransformIdList;
        public enum BoneConnectionMode
        {
            // line only.
            // ラインのみ
            Line = 0,

            //Automatically mesh connection according to the interval of Transform.
            // Transformの間隔に従い自動でメッシュ接続  按照Transform间隔自动网格连接
            AutomaticMesh = 1,

            //Generate meshes in the order of Transforms registered in RootList and connect the beginning and end in a loop.
            // RootListに登録されたTransformの順にメッシュを生成し、最初と最後をループ状に繋げる 按RootList中注册的Transform顺序生成网格,把开始和最后连接成环状
            SequentialLoopMesh = 2,

            // Generate meshes in the order of Transforms registered in RootList, but do not connect the beginning and end.
            // RootListに登録されたTransformの順にメッシュを生成するが最初と最後を繋げない
            SequentialNonLoopMesh = 3,
        }
        public BoneConnectionMode boneConnectionMode = BoneConnectionMode.Line;
        public List<int> collisionBoneIndexList; // BoneSpringのコリジョン有効Transformインデックスリスト BoneSpring碰撞有效变换索引列表

        // Common -------------------------------------------------------------
        // Transform情報
        // 通常メッシュはrenderTransorm100%のスキニングとして扱われる
        public List<Transform> transformList; // skin bonesは[0]～skinBoneCountまで
        public List<int> transformIdList;
        public List<int> transformParentIdList; // 親ID(0=なし)
        public List<FixedList512Bytes<int>> transformChildIdList; // 子IDリスト
        public NativeArray<float3> transformPositions;
        public NativeArray<quaternion> transformRotations;
        public NativeArray<float3> transformLocalPositins;
        public NativeArray<quaternion> transformLocalRotations;
        public NativeArray<float3> transformScales;
        public NativeArray<quaternion> transformInverseRotations;
        public int renderTransformIndex; // 描画基準トランスフォーム
        public float4x4 initRenderLocalToWorld; // 初期化時の基準マトリックス(LtoW) 初始化时的基准矩阵
        public float4x4 initRenderWorldtoLocal; // 初期化時の基準マトリックス(WtoL) 初始化时的基准矩阵
        public quaternion initRenderRotation; // 初期化時の基準回転 初始化时的基准旋转
        public float3 initRenderScale; // 初期化時の基準スケール 初始化时的基准比例

        public bool IsSuccess() => result.IsSuccess();
        public bool IsFaild() => result.IsFaild();
        public int TransformCount => transformList?.Count ?? 0;
        public bool HasMeshDataArray => meshDataArray.Length > 0;
        public bool HasLocalPositions => localPositions.IsCreated;

        //static readonly ProfilerMarker initProfiler = new ProfilerMarker("Render Setup");

        //=========================================================================================
        public RenderSetupData() { }

        /// <summary>
        /// レンダラーから基本情報を作成する（メインスレッドのみ） 从渲染器创建基本信息（仅主线程）
        /// タイプはMeshになる 类型为网格
        /// </summary>
        /// <param name="ren"></param>
        public RenderSetupData(Renderer ren)
        {
            //using (initProfiler.Auto())
            {
                result.Clear();
                // Meshタイプに設定
                setupType = SetupType.MeshCloth;

                if (ren == null)
                {
                    Develop.LogWarning("Renderer is null!");
                    result.SetError(Define.Result.RenderSetup_InvalidSource);
                    return;
                }

                name = ren.name;

                var sren = ren as SkinnedMeshRenderer;

                hasSkinnedMesh = sren ? true : false;
                hasBoneWeight = false;
                skinRenderer = sren;
                Mesh mesh;

                // 必要なトランスフォーム情報
                // トランスフォームのクラスとインスタンスIDを収集する
                // 描画の基準トランスフォーム
                var renderTransform = ren.transform;

                if (sren)
                {
                    // bones
                    // このスキニングボーンの取得が特に重くメモリアロケーションも頻発する問題児
                    // しかし回避方法がないため現状やむなし
                    var bones = sren.bones;
                    if (bones == null || bones.Length == 0)
                    {
                        // ブレンドシェイプではボーンや頂点ウエイトが無くてもSkinnedMeshRendererが利用される
                        // ブレンドシェイプの機能がSkinnedMeshRendererにしか無いため
                        // そのため、このようなケースではSkinnedMeshRendererであるが通常メッシュとして扱うことにする
                        // bones
                        skinBoneCount = 1;
                        transformList = new List<Transform>(skinBoneCount);
                        transformList.Add(renderTransform);

                        // rootBone
                        skinRootBoneIndex = 0;

                        // render
                        renderTransformIndex = 0;
                    }
                    else
                    {
                        skinBoneCount = bones.Length;
                        transformList = new List<Transform>(skinBoneCount + 2);
                        transformList.AddRange(bones);

                        // rootBone
                        var rootBone = sren.rootBone ? sren.rootBone : renderTransform;
                        skinRootBoneIndex = transformList.Count;
                        transformList.Add(rootBone);

                        // render
                        renderTransformIndex = transformList.Count;
                        transformList.Add(renderTransform);

                        hasBoneWeight = true;
                    }
                }
                else
                {
                    // bones
                    skinBoneCount = 1;
                    transformList = new List<Transform>(skinBoneCount);
                    transformList.Add(renderTransform);

                    // rootBone
                    skinRootBoneIndex = 0;

                    // render
                    renderTransformIndex = 0;
                }

                // トランスフォーム情報の読み取り
                ReadTransformInformation(includeChilds: false);

                // bindpose / weights
                if (sren)
                {
                    mesh = sren.sharedMesh;
                    if (mesh == null)
                    {
                        Develop.LogWarning("SkinnedMeshRenderer.sharedMesh is null!");
                        result.SetError(Define.Result.RenderSetup_NoMeshOnRenderer);
                        return;
                    }

                    if (hasBoneWeight)
                    {
                        bindPoseList = new List<Matrix4x4>(mesh.bindposes);

                        // どうもコピーを作らないとダメらしい..
                        // ※具体的にはメッシュのクローンを作成したときに壊れる
                        var weightArray = mesh.GetAllBoneWeights();
                        var perVertexArray = mesh.GetBonesPerVertex();
                        boneWeightArray = new NativeArray<BoneWeight1>(weightArray, Allocator.Persistent);
                        bonesPerVertexArray = new NativeArray<byte>(perVertexArray, Allocator.Persistent);

                        // ５ボーン以上を利用する頂点ウエイトは警告とする。一応無効となるだけで動くのでエラーにはしない。
                        int vcnt = mesh.vertexCount;
                        using var bonesPerVertexResult = new NativeReference<Define.Result>(Allocator.TempJob);
                        var job = new VertexWeight5BoneCheckJob()
                        {
                            vcnt = vcnt,
                            bonesPerVertexArray = bonesPerVertexArray,
                            result = bonesPerVertexResult,
                        };
                        job.Run();
                        result.SetWarning(bonesPerVertexResult.Value);
                    }
                    else
                    {
                        bindPoseList = new List<Matrix4x4>(1);
                        bindPoseList.Add(Matrix4x4.identity);
                    }
                }
                else
                {
                    var filter = ren.GetComponent<MeshFilter>();
                    if (filter == null)
                    {
                        result.SetError(Define.Result.RenderSetup_InvalidSource);
                        return;
                    }
                    mesh = filter.sharedMesh;
                    if (mesh == null)
                    {
                        Develop.LogWarning("MeshFilter.sharedMesh is null!");
                        result.SetError(Define.Result.RenderSetup_NoMeshOnRenderer);
                        return;
                    }

                    bindPoseList = new List<Matrix4x4>(1);
                    bindPoseList.Add(Matrix4x4.identity);
                    meshFilter = filter;
                }

                if (mesh.isReadable == false)
                {
                    result.SetError(Define.Result.RenderSetup_Unreadable);
                    return;
                }
                if (mesh.vertexCount > 65535)
                {
                    result.SetError(Define.Result.RenderSetup_Over65535vertices);
                    return;
                }

                // スレッドでメッシュデータを構築するためにオリジナルのMeshDataを取得する
                // mesh data array
                meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);

                // rendererとmeshを記録
                renderer = ren;
                originalMesh = mesh;
                vertexCount = mesh.vertexCount;

                // 完了
                result.SetSuccess();
            }
        }

        [BurstCompile]
        struct VertexWeight5BoneCheckJob : IJob
        {
            public int vcnt;

            [Unity.Collections.ReadOnly]
            public NativeArray<byte> bonesPerVertexArray;

            [Unity.Collections.WriteOnly]
            public NativeReference<Define.Result> result;

            public void Execute()
            {
                result.Value = Define.Result.None;

                for (int i = 0; i < vcnt; i++)
                {
                    if (bonesPerVertexArray[i] >= 5)
                    {
                        result.Value = Define.Result.RenderMesh_VertexWeightIs5BonesOrMore;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// ルートボーンリストから基本情報を作成する（メインスレッドのみ） 从根骨骼列表创建基本信息（仅限主线程）
        /// タイプはBoneになる 类型是Bone
        /// </summary>
        /// <param name="renderTransform"></param>
        /// <param name="rootTransforms"></param>
        public RenderSetupData(
            SetupType setType,
            Transform renderTransform,
            List<Transform> rootTransforms,
            List<Transform> collisionBones,
            BoneConnectionMode connectionMode = BoneConnectionMode.Line,
            string name = "(no name)"
            )
        {
            result.Clear();

            //using (initProfiler.Auto())
            try
            {
                // Boneタイプに設定
                setupType = setType;

                // 接続モード
                boneConnectionMode = connectionMode;

                if (renderTransform == null)
                {
                    //Debug.LogWarning("render transform is null!");
                    result.SetError(Define.Result.RenderSetup_InvalidSource);
                    return;
                }
                if (rootTransforms == null || rootTransforms.Count == 0)
                {
                    //Debug.LogWarning("rootBones is empty!");
                    result.SetError(Define.Result.RenderSetup_InvalidSource);
                    return;
                }

                this.name = name;

                // 必要なトランスフォーム情報  所需的变换信息
                var indexDict = new Dictionary<Transform, int>(256);
                transformList = new List<Transform>(256);

                // root以下をすべて登録する 注册根以下全部
                var stack = new Stack<Transform>(256);
                foreach (var t in rootTransforms)
                    stack.Push(t);
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    if (indexDict.ContainsKey(t))
                        continue;

                    // 登録
                    int index = transformList.Count;
                    transformList.Add(t);
                    indexDict.Add(t, index);

                    // child
                    int cnt = t.childCount;
                    for (int i = 0; i < cnt; i++)
                    {
                        stack.Push(t.GetChild(i));
                    }
                }

                // root transform id
                rootTransformIdList = new List<int>(rootTransforms.Count);
                foreach (var t in rootTransforms)
                {
                    rootTransformIdList.Add(t.GetInstanceID());
                }

                // collision transform (use BoneSpring)
                if (collisionBones != null)
                {
                    collisionBoneIndexList = new List<int>(collisionBones.Count);
                    foreach (var t in collisionBones)
                    {
                        if (t)
                        {
                            int index = transformList.IndexOf(t);
                            collisionBoneIndexList.Add(index);
                            //Debug.Log($"collision bones:{t.name}, index:{index}");
                        }
                    }
                }

                // スキニングボーン数 蒙皮骨骼数
                skinBoneCount = transformList.Count;

                // レンダートランスフォームを最後に追加 最后添加渲染变换
                renderTransformIndex = transformList.Count;
                transformList.Add(renderTransform);

                // Debug.Log("中心本地坐标：" + renderTransform.localPosition);

                // トランスフォーム情報の読み取り 读取变换信息
                ReadTransformInformation(includeChilds: true);

                // 完了
                result.SetSuccess();
            }
            catch (MagicaClothProcessingException)
            {
                if (result.IsError() == false)
                    result.SetError(Define.Result.RenderSetup_UnknownError);
                result.DebugLog();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                result.SetError(Define.Result.RenderSetup_Exception);
            }
        }

        /// <summary>
        /// トランスフォーム情報の読み取り（メインスレッドのみ）
        /// この情報だけはキャラクターが動く前に取得する必要がある
        /// 读取变换信息（仅限主线程）
        /// 只有这个信息需要在角色移动之前取得
        /// </summary>
        void ReadTransformInformation(bool includeChilds)
        {
            int tcnt = transformList.Count;
            using var transformArray = new TransformAccessArray(transformList.ToArray());
            transformPositions = new NativeArray<float3>(tcnt, Allocator.Persistent);
            transformRotations = new NativeArray<quaternion>(tcnt, Allocator.Persistent);
            transformLocalPositins = new NativeArray<float3>(tcnt, Allocator.Persistent);
            transformLocalRotations = new NativeArray<quaternion>(tcnt, Allocator.Persistent);
            transformScales = new NativeArray<float3>(tcnt, Allocator.Persistent);
            transformInverseRotations = new NativeArray<quaternion>(tcnt, Allocator.Persistent);
            var job = new ReadTransformJob()
            {
                positions = transformPositions,
                rotations = transformRotations,
                scales = transformScales,
                localPositions = transformLocalPositins,
                localRotations = transformLocalRotations,
                inverseRotations = transformInverseRotations,
            };
            // シミュレーション以外でワーカーを消費したくないのでRun()版にしておく
            // 因为不想在模拟以外消费工作人员，所以预先制作Run（）版
            job.RunReadOnly(transformArray);

            // 初期センタートランスフォームを別途コピーしておく
            // 单独复制初始中心变换
            initRenderLocalToWorld = GetRendeerLocalToWorldMatrix();
            initRenderWorldtoLocal = math.inverse(initRenderLocalToWorld);
            initRenderRotation = transformRotations[renderTransformIndex];
            initRenderScale = transformScales[renderTransformIndex];

            // id
            transformIdList = new List<int>(tcnt);
            transformParentIdList = new List<int>(tcnt);
            for (int i = 0; i < tcnt; i++)
            {
                var t = transformList[i];
                transformIdList.Add(t?.GetInstanceID() ?? 0);
                transformParentIdList.Add(t?.parent?.GetInstanceID() ?? 0);
            }

            // child id
            if (includeChilds)
            {
                transformChildIdList = new List<FixedList512Bytes<int>>(tcnt);
                for (int i = 0; i < tcnt; i++)
                {
                    var t = transformList[i];
                    var clist = new FixedList512Bytes<int>();
                    if (t?.childCount > 0)
                    {
                        for (int j = 0; j < t.childCount; j++)
                        {
                            var ct = t.GetChild(j);
                            clist.Add(ct.GetInstanceID());
                        }
                    }
                    transformChildIdList.Add(clist);
                }
            }
        }

        /// <summary>
        /// 最低限のTransform情報を収集する
        /// </summary>
        [BurstCompile]
        struct ReadTransformJob : IJobParallelForTransform
        {
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> positions;
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> rotations;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> scales;
            [Unity.Collections.WriteOnly]
            public NativeArray<float3> localPositions;
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> localRotations;
            [Unity.Collections.WriteOnly]
            public NativeArray<quaternion> inverseRotations;

            public void Execute(int index, TransformAccess transform)
            {
                if (transform.isValid == false)
                    return;

                var pos = transform.position;
                var rot = transform.rotation;
                float4x4 LtoW = transform.localToWorldMatrix;

                positions[index] = pos;
                rotations[index] = rot;
                localPositions[index] = transform.localPosition;
                localRotations[index] = transform.localRotation;

                // マトリックスから正確なスケール値を算出する（これはTransform.lossyScaleと等価）
                // 根据矩阵计算准确的比例值（这与Transform.lossyScale等价）
                var irot = math.inverse(rot);
                var m2 = math.mul(new float4x4(irot, float3.zero), LtoW);
                var scl = new float3(m2.c0.x, m2.c1.y, m2.c2.z);
                scales[index] = scl;

                // 逆回転
                // 反向旋转
                inverseRotations[index] = irot;
            }
        }

        public void Dispose()
        {
            // Pre-Build DeserializeManager管理中は破棄させない
            if (isManaged)
                return;

            bonesPerVertexArray.MC2DisposeSafe();
            boneWeightArray.MC2DisposeSafe();
            localPositions.MC2DisposeSafe();
            localNormals.MC2DisposeSafe();

            transformPositions.MC2DisposeSafe();
            transformRotations.MC2DisposeSafe();
            transformLocalPositins.MC2DisposeSafe();
            transformLocalRotations.MC2DisposeSafe();
            transformScales.MC2DisposeSafe();
            transformInverseRotations.MC2DisposeSafe();

            // MeshDataArrayはメインスレッドのみDispose()可能
            if (setupType == SetupType.MeshCloth)
            {
                if (meshDataArray.Length > 0)
                    meshDataArray.Dispose();
            }
        }

        public void GetUsedTransform(HashSet<Transform> transformSet)
        {
            if (transformList != null)
            {
                foreach (var t in transformList)
                    transformSet.Add(t);
            }
        }

        public void ReplaceTransform(Dictionary<int, Transform> replaceDict)
        {
            if (rootTransformIdList != null)
            {
                for (int i = 0; i < rootTransformIdList.Count; i++)
                {
                    int id = rootTransformIdList[i];
                    if (replaceDict.ContainsKey(id))
                    {
                        rootTransformIdList[i] = replaceDict[id].GetInstanceID();
                    }
                }
            }
            for (int i = 0; i < TransformCount; i++)
            {
                int id = transformIdList[i];
                if (replaceDict.ContainsKey(id))
                {
                    var t = replaceDict[id];
                    transformIdList[i] = t.GetInstanceID();
                    transformList[i] = t;
                }
                int pid = transformParentIdList[i];
                if (replaceDict.ContainsKey(pid))
                {
                    var t = replaceDict[pid];
                    transformParentIdList[i] = t.GetInstanceID();
                }

                if (transformChildIdList != null)
                {
                    var cidlist = transformChildIdList[i];
                    for (int j = 0; j < cidlist.Length; j++)
                    {
                        int cid = cidlist[j];
                        if (replaceDict.ContainsKey(cid))
                        {
                            var t = replaceDict[cid];
                            cidlist[j] = t.GetInstanceID();
                        }
                    }
                    transformChildIdList[i] = cidlist;
                }
            }
        }

        //=========================================================================================
        /// <summary>
        /// 描画基準トランスフォームを取得する
        /// </summary>
        /// <returns></returns>
        public Transform GetRendeerTransform()
        {
            return transformList[renderTransformIndex];
        }

        public int GetRenderTransformId()
        {
            return transformIdList[renderTransformIndex];
        }

        public float4x4 GetRendeerLocalToWorldMatrix()
        {
            var pos = transformPositions[renderTransformIndex];
            var rot = transformRotations[renderTransformIndex];
            var scl = transformScales[renderTransformIndex];
            return float4x4.TRS(pos, rot, scl);
        }

        /// <summary>
        /// スキンレンダラーのルートトランスフォームを取得する
        /// 获取蒙皮渲染器的根变换
        /// </summary>
        /// <returns></returns>
        public Transform GetSkinRootTransform()
        {
            return transformList[skinRootBoneIndex];
        }

        public int GetSkinRootTransformId()
        {
            return transformIdList[skinRootBoneIndex];
        }

        public int GetTransformIndexFromId(int id)
        {
            return transformIdList.IndexOf(id);
        }

        /// <summary>
        /// 指定indexの親トランスフォームのインデックスを返す(-1=なし)
        /// </summary>
        /// <param name="index"></param>
        /// <param name="centerExcluded">true=センタートランスフォームは除外する 排除中心变换</param>
        /// <returns>なし(-1)</returns>
        public int GetParentTransformIndex(int index, bool centerExcluded)
        {
            int pid = transformParentIdList[index];
            int i = transformIdList.IndexOf(pid);
            if (centerExcluded && i == renderTransformIndex)
                i = -1;
            return i;
        }

        //=========================================================================================
        /// <summary>
        /// オリジナルメッシュのボーンウエイトをBoneWeight構造体のNativeArrayで取得する
        /// 在BoneWeight结构的NativeArray中获取原始网格的骨骼权重
        /// </summary>
        /// <param name="weights"></param>
        public void GetBoneWeightsRun(NativeArray<BoneWeight> weights)
        {
            var job = new GetBoneWeightJos()
            {
                vcnt = vertexCount,
                bonesPerVertexArray = bonesPerVertexArray,
                boneWeightArray = boneWeightArray,
                boneWeights = weights,
            };
            job.Run();
        }

        [BurstCompile]
        struct GetBoneWeightJos : IJob
        {
            public int vcnt;

            [Unity.Collections.ReadOnly]
            public NativeArray<byte> bonesPerVertexArray;
            [Unity.Collections.ReadOnly]
            public NativeArray<BoneWeight1> boneWeightArray;

            [Unity.Collections.WriteOnly]
            public NativeArray<BoneWeight> boneWeights;

            public void Execute()
            {
                int index = 0;
                for (int i = 0; i < vcnt; i++)
                {
                    var bw = new BoneWeight();

                    var cnt = bonesPerVertexArray[i];
                    for (int j = 0; j < cnt; j++)
                    {
                        var bw1 = boneWeightArray[index];
                        index++;

                        switch (j)
                        {
                            case 0:
                                bw.weight0 = bw1.weight;
                                bw.boneIndex0 = bw1.boneIndex;
                                break;
                            case 1:
                                bw.weight1 = bw1.weight;
                                bw.boneIndex1 = bw1.boneIndex;
                                break;
                            case 2:
                                bw.weight2 = bw1.weight;
                                bw.boneIndex2 = bw1.boneIndex;
                                break;
                            case 3:
                                bw.weight3 = bw1.weight;
                                bw.boneIndex3 = bw1.boneIndex;
                                break;
                        }
                    }

                    boneWeights[i] = bw;
                }
            }
        }
    }
}
