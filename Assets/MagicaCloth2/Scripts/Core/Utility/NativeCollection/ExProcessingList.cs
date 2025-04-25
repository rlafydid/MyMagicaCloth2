// Magica Cloth 2.
// Copyright (c) 2023 MagicaSoft.
// https://magicasoft.jp
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace MagicaCloth2
{
    /// <summary>
    /// １つのバッファに並列にデータを書き込めるようにするための構造。
    /// カウンターをアトミック操作することによりその開始インデックスを管理する。
    /// 用于并行地将数据写入一个缓冲区的结构。
    /// 通过原子操作计数器来管理其开始索引。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public unsafe class ExProcessingList<T> : IDisposable, IValid where T : struct
    {
        /// <summary>
        /// バッファの現在のデータ数をカウントするためのカウンター
        /// 用于计数缓冲器当前数据数的计数器
        /// </summary>
        public NativeReference<int> Counter;

        /// <summary>
        /// データバッファ
        /// 数据缓冲器
        /// </summary>
        public NativeArray<T> Buffer;

        //=========================================================================================
        public void Dispose()
        {
            if (Counter.IsCreated)
                Counter.Dispose();
            if (Buffer.IsCreated)
                Buffer.Dispose();
        }

        public bool IsValid()
        {
            return Counter.IsCreated;
        }

        //=========================================================================================
        public ExProcessingList()
        {
            Counter = new NativeReference<int>(Allocator.Persistent);
        }

        //=========================================================================================
        /// <summary>
        /// キャパシティが収まるようにバッファを拡張する。
        /// すでに容量が確保できている場合は何もしない。
        /// 扩展缓冲区以容纳容量。
        /// 已经确保容量的情况下什么都不做。
        /// </summary>
        /// <param name="capacity"></param>
        public void UpdateBuffer(int capacity)
        {
            if (Buffer.IsCreated == false || Buffer.Length < capacity)
            {
                if (Buffer.IsCreated)
                    Buffer.Dispose();
                Buffer = new NativeArray<T>(capacity, Allocator.Persistent);
            }
        }

        /// <summary>
        /// ジョブスケジュール用のカウントintポインターを取得する
        /// </summary>
        /// <param name="counter"></param>
        /// <returns></returns>
        public int* GetJobSchedulePtr()
        {
            return (int*)Counter.GetUnsafePtrWithoutChecks();
        }

        public override string ToString()
        {
            int counter = Counter.IsCreated ? Counter.Value : 0;
            int bufferLength = Buffer.IsCreated ? Buffer.Length : 0;
            return $"ExProcessingList BufferLength:{bufferLength} Counter:{counter}";
        }
    }
}
