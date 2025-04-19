using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public enum EMirrorAxis
{
    X,
    Y,
    Z
}

public class Mirror : MonoBehaviour
{
    public Transform SourceBone;

    public Transform MirrorBone;

    public Transform Root;

    public Transform Collider;
    public Transform MirrorCollider;

    public List<EMirrorAxis> MirrorPosAxisList;

    public List<EMirrorAxis> MirrorRotationAxisList;

    public EMirrorAxis MirrorAxis = EMirrorAxis.Y;

    private Vector3 sourceForward;
    private Vector3 mirrorForward;


    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        CalculateMirror();

        if (MirrorCollider)
        {
            Debug.DrawLine(Collider.transform.position, Collider.position + Collider.forward * 4);
            Debug.DrawLine(MirrorCollider.position, MirrorCollider.position + mirrorForward * 4);
        }

        MirrorPositionRelativeToRoot();
        CalculateMirror();

        /*
        Vector3 Normal = Vector3.forward;
        // Vector3 forward =  Quaternion.Inverse(Root.rotation) * Collider.forward;
        Vector3 forward = Collider.forward;

        Vector3 t1 = Root.position;
        Vector3 t2 = Collider.position;
        t1.y = 0;
        t2.y = 0;
        Normal = Vector3.Cross(Collider.up, (t1 - t2).normalized);

        Debug.DrawLine(Vector3.zero, Normal * 4);
        mirrorForward = Vector3.Dot(forward, Normal) * 2 * Normal  - forward;
        MirrorCollider.forward = mirrorForward;
        */
    }

    private void OnGUI()
    {
        if (GUILayout.Button("生成镜像"))
        {

        }

    }

    public void MirrorPositionRelativeToRoot()
    {
        // Step 1: 转换到root的局部坐标系
        Vector3 localPos = Root.InverseTransformPoint(Collider.position);

        foreach (var axis in MirrorPosAxisList)
        {
            // Step 2: 执行轴向镜像
            switch (axis)
            {
                case EMirrorAxis.X:
                    localPos.x = -localPos.x;
                    break;
                case EMirrorAxis.Y:
                    localPos.y = -localPos.y;
                    break;
                case EMirrorAxis.Z:
                    localPos.z = -localPos.z;
                    break;
            }
        }

        // Step 3: 转换回世界坐标系
        Vector3 worldPos = Root.TransformPoint(localPos);
        MirrorCollider.transform.position = worldPos;
    }


    // 基于 Root 的旋转镜像（世界坐标系）
    public Quaternion MirrorRotationRelativeToRoot(
        Quaternion originalRotWorld,
        Transform root
    )
    {
        // Step 1: 将旋转转换到 Root 的局部坐标系
        Quaternion localRot = Quaternion.Inverse(root.rotation) * originalRotWorld;

        Vector3 axis = new Vector3(1, 0, 0);

        // Step 2: 在 Root 的局部空间计算镜像旋转
        Quaternion mirroredLocalRot = MirrorRotationQuaternion(localRot, axis);

        // Step 3: 转换回世界坐标系
        return root.rotation * mirroredLocalRot;
    }

// 反射变换核心逻辑（参数 mirrorNormal 需为世界坐标系方向）
    public Quaternion MirrorRotationQuaternion(Quaternion originalRot, Vector3 mirrorNormalWorld)
    {
        // 构造反射矩阵（基于世界坐标系法线）
        mirrorNormalWorld.Normalize();
        Matrix4x4 reflectionMatrix = Matrix4x4.identity;
        reflectionMatrix.m00 = 1 - 2 * mirrorNormalWorld.x * mirrorNormalWorld.x;
        reflectionMatrix.m01 = -2 * mirrorNormalWorld.x * mirrorNormalWorld.y;
        reflectionMatrix.m02 = -2 * mirrorNormalWorld.x * mirrorNormalWorld.z;

        reflectionMatrix.m10 = -2 * mirrorNormalWorld.y * mirrorNormalWorld.x;
        reflectionMatrix.m11 = 1 - 2 * mirrorNormalWorld.y * mirrorNormalWorld.y;
        reflectionMatrix.m12 = -2 * mirrorNormalWorld.y * mirrorNormalWorld.z;

        reflectionMatrix.m20 = -2 * mirrorNormalWorld.z * mirrorNormalWorld.x;
        reflectionMatrix.m21 = -2 * mirrorNormalWorld.z * mirrorNormalWorld.y;
        reflectionMatrix.m22 = 1 - 2 * mirrorNormalWorld.z * mirrorNormalWorld.z;

        // 应用反射变换
        Matrix4x4 rotMatrix = Matrix4x4.Rotate(originalRot);
        Matrix4x4 mirroredMatrix = reflectionMatrix * rotMatrix * reflectionMatrix;

        return mirroredMatrix.rotation;
    }

    // 获取 Root 局部坐标系下的镜像轴（转换为世界方向）
    private Vector3 GetMirrorAxisWorld(EMirrorAxis axis)
    {
        // switch (axis)
        // {
        //     case EMirrorAxis.X: return Root.right;    // 使用 Root 的局部 X 轴
        //     case EMirrorAxis.Y: return Root.up;       // 使用 Root 的局部 Y 轴
        //     case EMirrorAxis.Z: return Root.forward;  // 使用 Root 的局部 Z 轴
        //     default: return Vector3.up;
        // }
        switch (axis)
        {
            case EMirrorAxis.X: return Vector3.right;    // 使用 Root 的局部 X 轴
            case EMirrorAxis.Y: return Vector3.up;       // 使用 Root 的局部 Y 轴
            case EMirrorAxis.Z: return Vector3.forward;  // 使用 Root 的局部 Z 轴
            default: return Vector3.up;
        }
    }

    private void CalculateMirror()
    {
        // 旋转镜像（基于Root的局部坐标系）
        MirrorCollider.transform.rotation = MirrorRotationRelativeToRoot(
            Collider.transform.rotation,
            Root
        );
    }
/*
// 基于Root局部坐标系的旋转镜像
    public Quaternion MirrorRotationRelativeToRoot(
        Quaternion originalRotWorld,
        Transform root,
        EMirrorAxis axis1)
    {
        // Step 1: 转换到Root的局部旋转
        Quaternion localRot = Quaternion.Inverse(root.rotation) * originalRotWorld;
        Matrix4x4 reflectionMatrix = Matrix4x4.identity;

        // foreach (var axis in MirrorRotationAxisList)
        // {
        //     // Step 2: 在局部空间构造反射矩阵
        //     switch (axis)
        //     {
        //         case EMirrorAxis.X:
        //             reflectionMatrix.m11 = -1; // Y轴取反
        //             reflectionMatrix.m22 = -1; // Z轴取反
        //             break;
        //         case EMirrorAxis.Y:
        //             reflectionMatrix.m00 = -1; // X轴取反
        //             reflectionMatrix.m22 = -1; // Z轴取反
        //             break;
        //         case EMirrorAxis.Z:
        //             reflectionMatrix.m00 = -1; // X轴取反
        //             reflectionMatrix.m11 = -1; // Y轴取反
        //             break;
        //     }
        // }
        reflectionMatrix.m00 = -1; // X轴取反
        // reflectionMatrix.m11 = 1; // Y轴取反
        reflectionMatrix.m22 = -1; // Z轴取反


        // Step 3: 应用反射矩阵到局部旋转
        Matrix4x4 rotMatrix = Matrix4x4.Rotate(localRot);
        Matrix4x4 mirroredMatrix = reflectionMatrix * rotMatrix;

        // Step 4: 转换回世界坐标系
        return root.rotation * mirroredMatrix.rotation;
    }
*/
}
