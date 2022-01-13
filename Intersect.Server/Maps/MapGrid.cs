﻿using System;
using System.Collections.Generic;

using Intersect.Server.Database;

using Newtonsoft.Json.Linq;

namespace Intersect.Server.Maps
{

    public partial class MapGrid
    {

        private readonly int mMyIndex;

        public long Height;

        private Point mBotRight = new Point(0, 0);

        private Guid[,] mClientData;

        private string[,] mEditorData;

        private int[] mTmpMaps;

        private Point mTopLeft = new Point(0, 0);

        public Guid[,] MyGrid;

        public List<Guid> MyMaps = new List<Guid>();

        public long Width;

        public long XMax;

        public long XMin;

        public long YMax;

        public long YMin;

        public MapGrid(Guid startMapId, int myGridIndex)
        {
            mMyIndex = myGridIndex;
            MapController.Get(startMapId).MapGrid = myGridIndex;
            MapController.Get(startMapId).MapGridX = 0;
            MapController.Get(startMapId).MapGridY = 0;
            MyMaps.Clear();
            CalculateBounds(MapController.Get(startMapId), 0, 0);

            Width = mBotRight.X - mTopLeft.X + 1;
            Height = mBotRight.Y - mTopLeft.Y + 1;
            var xoffset = mTopLeft.X;
            var yoffset = mTopLeft.Y;
            XMin = mTopLeft.X - xoffset;
            YMin = mTopLeft.Y - yoffset;
            XMax = mBotRight.X - xoffset + 1;
            YMax = mBotRight.Y - yoffset + 1;
            MyGrid = new Guid[Width, Height];
            var tmpMaps = new List<Guid>();
            tmpMaps.AddRange(MyMaps.ToArray());
            for (var x = XMin; x < XMax; x++)
            {
                for (var y = YMin; y < YMax; y++)
                {
                    MyGrid[x, y] = Guid.Empty;
                    for (var i = 0; i < tmpMaps.Count; i++)
                    {
                        if (MapController.Get(tmpMaps[i]).MapGridX + Math.Abs(mTopLeft.X) == x &&
                            MapController.Get(tmpMaps[i]).MapGridY + Math.Abs(mTopLeft.Y) == y)
                        {
                            MyGrid[x, y] = tmpMaps[i];
                            MapController.Get(tmpMaps[i]).MapGrid = myGridIndex;
                            MapController.Get(tmpMaps[i]).MapGridX = (int) x;
                            MapController.Get(tmpMaps[i]).MapGridY = (int) y;
                            tmpMaps.RemoveAt(i);

                            break;
                        }
                    }
                }
            }

            foreach (var s in tmpMaps)
            {
                MyMaps.Remove(s);
            }
        }

        private void CalculateBounds(MapController map, int x, int y)
        {
            var maps = new Stack<Tuple<MapController, int, int>>();
            maps.Push(new Tuple<MapController, int, int>(map, x, y));
            while (maps.Count > 0)
            {
                var curMap = maps.Pop();
                map = curMap.Item1;
                x = curMap.Item2;
                y = curMap.Item3;

                if (HasMap(map.Id) || DbInterface.GridsContain(map.Id))
                {
                    continue;
                }

                MyMaps.Add(map.Id);
                map.MapGridX = x;
                map.MapGridY = y;
                if (x < mTopLeft.X)
                {
                    mTopLeft.X = x;
                }

                if (y < mTopLeft.Y)
                {
                    mTopLeft.Y = y;
                }

                if (x > mBotRight.X)
                {
                    mBotRight.X = x;
                }

                if (y > mBotRight.Y)
                {
                    mBotRight.Y = y;
                }

                if (MapController.Lookup.Keys.Contains(map.Up) && MapController.Get(map.Up).Down == map.Id)
                {
                    maps.Push(new Tuple<MapController, int, int>(MapController.Get(map.Up), x, y - 1));
                }

                if (MapController.Lookup.Keys.Contains(map.Down) && MapController.Get(map.Down).Up == map.Id)
                {
                    maps.Push(new Tuple<MapController, int, int>(MapController.Get(map.Down), x, y + 1));
                }

                if (MapController.Lookup.Keys.Contains(map.Left) && MapController.Get(map.Left).Right == map.Id)
                {
                    maps.Push(new Tuple<MapController, int, int>(MapController.Get(map.Left), x - 1, y));
                }

                if (MapController.Lookup.Keys.Contains(map.Right) && MapController.Get(map.Right).Left == map.Id)
                {
                    maps.Push(new Tuple<MapController, int, int>(MapController.Get(map.Right), x + 1, y));
                }
            }
        }

        public bool HasMap(Guid mapId)
        {
            return MyMaps.Contains(mapId);
        }

        public string[,] GetEditorData()
        {
            var data = new string[Width, Height];
            for (var x = XMin; x < XMax; x++)
            {
                for (var y = YMin; y < YMax; y++)
                {
                    var map = MapController.Get(MyGrid[x, y]);
                    if (map != null)
                    {
                        var obj = new JObject();
                        obj.Add("Guid", map.Id);
                        obj.Add("Name", map.Name);
                        obj.Add("Revision", map.Revision);
                        data[x, y] = obj.ToString();
                    }
                }
            }

            return data;
        }

        public Guid[,] GetClientData()
        {
            return MyGrid;
        }

    }

}
