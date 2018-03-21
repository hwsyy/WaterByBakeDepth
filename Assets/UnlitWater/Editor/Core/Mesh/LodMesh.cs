﻿using UnityEngine;
using System.Collections;
using UnityEditor;

namespace ASL.UnlitWater
{
    /// <summary>
    /// Lod网格
    /// 该类型网格根据传入的纹理，将自动在水岸与陆地的交界处生成最密集的网格，越远离海岸，网格越稀疏
    /// </summary>
    [System.Serializable]
    internal class LodMesh : IMeshGenerator
    {
        /// <summary>
        /// 不可见颜色，小于该值的颜色判断为不可见
        /// </summary>
        public const float kInVisibleColor = 0.01f;
        /// <summary>
        /// 边缘极差，极差大于该值判断为处于边缘
        /// </summary>
        public const float kEdgeRange = 0.4f;

        private LodMeshCell[,] m_Cells;

        public int cellSizeX;
        public int cellSizeZ;
        public float widthX;
        public float widthZ;
        //private float m_OffsetX;
        //private float m_OffsetZ;
        public int maxLod;
        public int samples = 2;
        public float uvDir;

        //private bool m_Support;

        //public LodMesh()
        //{
        //    //m_Cells = new LodMeshCell[xCells, zCells];
        //    //cellSizeX = xCells;
        //    //cellSizeZ = zCells;
        //    //widthX = xWidth;
        //    //widthZ = zWidth;
        //    //m_OffsetX = offsetX;
        //    //m_OffsetZ = offsetZ;
        //    //maxLod = maxLod;
        //    //samples = samples;
        //    //uvDir = uvDir;

        //    //if (xCells > 0 && zCells > 0 && zWidth > 0 && xWidth > 0 && maxLod >= 0 && samples >= 1)
        //    //    m_Support = true;
        //}

        public void DrawGUI()
        {
            widthX = Mathf.Max(0.01f, EditorGUILayout.FloatField("Width", widthX));
            widthZ = Mathf.Max(0.01f, EditorGUILayout.FloatField("Height", widthZ));
            cellSizeX = Mathf.Max(1, EditorGUILayout.IntField("CellWidth", cellSizeX));
            cellSizeZ = Mathf.Max(1, EditorGUILayout.IntField("CellHeight", cellSizeZ));
            uvDir = EditorGUILayout.Slider("UV水平方向", uvDir, 0, 360);
            maxLod = EditorGUILayout.IntSlider("最大Lod", maxLod, 0, 8);
            samples = EditorGUILayout.IntSlider("不可见三角剔除采样", samples, 1, 4); 
        }

        public void DrawSceneGUI(GameObject target, Vector2 offset, float rotY, float minHeight, float maxHeight)
        {
            UnlitWaterHandles.DrawUnlitWaterArea(
               target.transform.position + new Vector3(offset.x, 0, offset.y),
               Quaternion.Euler(0, rotY, 0), new Vector2(widthX, widthZ),
               new Vector2(minHeight, maxHeight), Color.green);

            UnlitWaterHandles.DrawUnlitWaterCells(
                    target.transform.position + new Vector3(offset.x, 0, offset.y),
                    Quaternion.Euler(0, rotY, 0), new Vector2(widthX, widthZ), cellSizeX, cellSizeZ, maxLod);

            float sz = Mathf.Max(widthX, widthZ) / 10;
            UnlitWaterHandles.DrawDirArrow(
                target.transform.position + new Vector3(offset.x, 0, offset.y), uvDir, sz,
                Color.cyan);
        }

        public void SetSize(Vector2 size)
        {
            widthX = size.x;
            widthZ = size.y;
        }

        public Vector2 GetSize()
        {
            return new Vector2(widthX, widthZ);
        }

        public Mesh GenerateMesh(Texture2D texture)
        {
            if (cellSizeX <= 0 || cellSizeZ <= 0 || widthX <= 0 || widthZ <= 0 || maxLod < 0 || samples < 1)
                return null;
            m_Cells = new LodMeshCell[cellSizeX, cellSizeZ];

            //根据贴图尺寸和单元格数量，计算分配给单个单元格的像素宽高
            int w = texture.width / cellSizeX;
            int h = texture.height / cellSizeZ;

            //计算Lod
            for (int i = 0; i < cellSizeX; i++)
            {
                for (int j = 0; j < cellSizeZ; j++)
                {
                    m_Cells[i, j] = new LodMeshCell(-widthX, -widthZ, i, j, widthX*2 / cellSizeX,
                        widthZ*2 / cellSizeZ);
                    //为单元格分配指定区域的像素并计算极差和平均值
                    m_Cells[i, j].Calculate(texture, i * w, j * h, w, h);
                    if (m_Cells[i, j].average < kInVisibleColor)
                    {
                        m_Cells[i, j].lod = -1;//如果单元格像素颜色平均值小于0.01，则判定该单元格基本上位于非水域内，则lod设置为-1，将不参与水网格的构建
                        continue;
                    }
                    if (m_Cells[i, j].range > kEdgeRange)//如果极差超过0.4，则判定该单元格同时包含水域和陆地，即岸边区域，应该给予最大lod
                        m_Cells[i, j].lod = maxLod;
                }
            }

            //根据上一步计算的结果，将最大lod单元格边上的格子设置lod递减
            for (int i = 0; i < cellSizeX; i++)
            {
                for (int j = 0; j < cellSizeZ; j++)
                {
                    LodMeshCell cell = m_Cells[i, j];
                    if (cell.lod == -1)
                        continue;
                    if (cell.lod != maxLod)
                        continue;
                    for (int lx = maxLod - 1, ly = 0; lx >= 0; lx--, ly++)
                    {
                        for (int lk = 0; lk <= ly; lk++)
                        {
                            if (lk == 0 && lx == 0)
                                continue;
                            int clod = maxLod - lx - lk;
                            //从最大lod处往外递减lod
                            SetNeighborLOD(i - lx, j - lk, cellSizeX, cellSizeZ, clod, m_Cells);
                            SetNeighborLOD(i + lx, j - lk, cellSizeX, cellSizeZ, clod, m_Cells);
                            SetNeighborLOD(i - lx, j + lk, cellSizeX, cellSizeZ, clod, m_Cells);
                            SetNeighborLOD(i + lx, j + lk, cellSizeX, cellSizeZ, clod, m_Cells);
                        }
                    }
                }
            }

            //根据Lod生成Mesh

            float p = Mathf.Pow(2, maxLod);
            float dtx = widthX*2 / cellSizeX / p;
            float dty = widthZ*2 / cellSizeZ / p;

            MeshVertexData cache = new MeshVertexData(cellSizeX * (int)p + 1, cellSizeZ * (int)p + 1, dtx, dty, -widthX, -widthZ);
            for (int i = 0; i < cellSizeX; i++)
            {
                for (int j = 0; j < cellSizeZ; j++)
                {
                    LodMeshCell cell = m_Cells[i, j];
                    if (cell.lod == -1)
                        continue;
                    int leftLod = i == 0 ? -1 : m_Cells[i - 1, j].lod;
                    int rightLod = i == m_Cells.GetLength(0) - 1 ? -1 : m_Cells[i + 1, j].lod;
                    int downLod = j == 0 ? -1 : m_Cells[i, j - 1].lod;
                    int upLod = j == m_Cells.GetLength(1) - 1 ? -1 : m_Cells[i, j + 1].lod;
                    cell.UpdateMesh(cache, leftLod, rightLod, upLod, downLod);
                }
            }
            //生成网格
            Mesh mesh = cache.Apply(texture, uvDir, samples);
            return mesh;
        }

        /// <summary>
        /// 设置相邻网格的Lod
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="cellx"></param>
        /// <param name="celly"></param>
        /// <param name="lod"></param>
        /// <param name="cells"></param>
        private void SetNeighborLOD(int i, int j, int cellx, int celly, int lod, LodMeshCell[,] cells)
        {
            if (i < 0)
                return;
            if (i >= cellx)
                return;
            if (j < 0)
                return;
            if (j >= celly)
                return;
            if (lod < 0)
                return;
            if (cells[i, j].lod < 0)
                return;
            if (lod <= cells[i, j].lod)
                return;
            cells[i, j].lod = lod;
        }
    }

}