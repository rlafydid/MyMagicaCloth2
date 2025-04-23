// Magica Cloth 2.
// Copyright (c) 2023 MagicaSoft.
// https://magicasoft.jp
using System;
using System.Collections.Generic;
using Unity.Collections;

namespace MagicaCloth2
{
    /// <summary>
    /// T型のデータリストを構築し要素ごとにそのスタートインデックスとデータカウンタを生成する
    /// 出力はT型のデータ配列と、要素ごとのスタートインデックスとカウンタが１つのuintにパックされた配列の２つ
    /// 构建T型数据列表，为每个元素生成其开始索引和数据计数器
    /// 输出是T型的数据排列，每个元素的开始索引和计数器被包装成一个uint的排列这两个
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class MultiDataBuilder<T> : IDisposable where T : unmanaged
    {
        int indexCount;

        public NativeParallelMultiHashMap<int, T> Map;

        //=========================================================================================
        public MultiDataBuilder(int indexCount, int dataCapacity)
        {
            this.indexCount = indexCount;
            Map = new NativeParallelMultiHashMap<int, T>(dataCapacity, Allocator.Persistent);
        }


        public void Dispose()
        {
            if (Map.IsCreated)
                Map.Dispose();
        }

        public int Count() => Map.Count();

        public int GetDataCount(int index)
        {
            if (Map.ContainsKey(index) == false)
                return 0;

            return Map.CountValuesForKey(index);
        }

        public void Add(int key, T data)
        {
            Map.Add(key, data);
        }

        //public int AddAndReturnIndex(int key, T data)
        //{
        //    int cnt = Map.CountValuesForKey(key);
        //    Map.Add(key, data);
        //    return cnt;
        //}

        public int CountValuesForKey(int key)
        {
            return Map.CountValuesForKey(key);
        }

        //=========================================================================================
        /// <summary>
        /// 内部HashMapのデータをT型配列と要素ごとのスタートインデックスとカウンタ配列の２つに分離して返す
        /// 出力はT型のデータ配列と、要素ごとのスタートインデックス(20bit)とカウンタ(12bit)を１つのuintにパックした配列となる
        /// 将内部HashMap的数据分离为T型数组、每个元素的开始索引和计数器数组两个来返回
        /// 输出为将T型数据排列、每个要素的开始索引（20bit）和计数器（12bit）封装在一个uint中的排列
        /// </summary>
        /// <returns></returns>
        public (T[], uint[]) ToArray()
        {
            if (Map.IsCreated == false || indexCount == 0)
                return (null, null);

            var indexArray = new uint[indexCount];
            var dataList = new List<T>(Map.Capacity);

            for (int i = 0; i < indexCount; i++)
            {
                int start = dataList.Count;
                int cnt = 0;

                if (Map.ContainsKey(i))
                {
                    foreach (var data in Map.GetValuesForKey(i))
                    {
                        dataList.Add(data);
                        cnt++;
                    }
                }

                indexArray[i] = DataUtility.Pack12_20(cnt, start);
            }

            return (dataList.ToArray(), indexArray);
        }

        public uint[] ToIndexArray()
        {
            (var _, uint[] indexArray) = ToArray();
            return indexArray;
        }

        /// <summary>
        /// 内部HashMapのデータをT型配列と要素ごとのスタートインデックス+カウンタの２つのNativeArrayに分離して返す
        /// 出力はT型のデータ配列と、要素ごとのスタートインデックス(20bit)とカウンタ(12bit)を１つのuintにパックした配列となる
        /// 将内部HashMap的数据分离为T型数组和每个元素的开始索引+计数器这两个NativeArray并返回
        /// 输出为将T型数据排列、每个要素的开始索引（20bit）和计数器（12bit）封装在一个uint中的排列
        /// </summary>
        /// <param name="indexArray"></param>
        /// <param name="dataArray"></param>
        public void ToNativeArray(out NativeArray<uint> indexArray, out NativeArray<T> dataArray)
        {
            indexArray = new NativeArray<uint>(indexCount, Allocator.Persistent);

            // 这不是相当于把所有
            var dataList = new List<T>(Map.Capacity);

            for (int i = 0; i < indexCount; i++)
            {
                int start = dataList.Count;
                int cnt = 0;

                //Map的Key是顶点索引，Value是Key它所连接的所有点
                if (Map.ContainsKey(i))
                {
                    foreach (var data in Map.GetValuesForKey(i))
                    {
                        dataList.Add(data);
                        cnt++;
                    }
                }

                indexArray[i] = DataUtility.Pack12_20(cnt, start); //开始索引 ~ 数量
            }

            dataArray = new NativeArray<T>(dataList.ToArray(), Allocator.Persistent); //按照粒子的连接顺序
        }
    }
}
