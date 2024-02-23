using ProtoBuf;
using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace RealisticGravity
{
    public class GridGravityData
    {
        public static int DIVISIONS;
        public static double[] angleList;
        private static int updateCtr = -1;
        public static bool updateFlag { private set; get; }
        private const int UPDATE_PERIOD = 300;

        public IMyCubeGrid grid;
        public PlanetManager.GravityPlanetData nearestPlanetData;
        private Vector3D planetPos;
        public IMyFaction faction;
        private Vector4 relationColor;
        private bool noShow;
        private IMyGps gps;

        private float camDist = float.MaxValue;
        private float angleOffset;
        private bool hasHighDetail;
        private int firstHighDetailIndex = -1;
        private int secondHighDetailIndex = -1;
        private Vector3[] orbitHighDetailPoints = new Vector3[DIVISIONS * 2 + 1];
        private float highDetailScaleFactor;

        private float[] orbitAngles = new float[DIVISIONS];
        private Vector3[] orbitPoints = new Vector3[DIVISIONS];
        private Vector3[] orbitMidPoints = new Vector3[DIVISIONS];
        private bool[] orbitPointsFlags = new bool[DIVISIONS];
        private float[] orbitPointsLineThicknesses = new float[DIVISIONS];

        public static readonly MyStringId weaponLaserId = MyStringId.GetOrCompute("WeaponLaser");

        public GridGravityData(IMyCubeGrid grid, PlanetManager.GravityPlanetData planetData)
        {
            this.grid = grid;
            this.nearestPlanetData = planetData;
            planetPos = planetData.planet.PositionComp.GetPosition();

            UpdateFaction();
            SetRelation();
        }

        public static GridGravityData RestoreGridGravDataFromString(string str)
        {
            string[] tokens = str.Split(';');
            if (tokens.Length == 3)
            {
                long gridId, planetId, factionId;
                if (long.TryParse(tokens[0], out gridId) && long.TryParse(tokens[1], out planetId) && PlanetManager.Instance.planetList.ContainsKey(planetId) && long.TryParse(tokens[2], out factionId))
                {
                    IMyCubeGrid grid = MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;
                    if (grid != null)
                    {
                        PlanetManager.GravityPlanetData planetData = PlanetManager.Instance.planetList[planetId];
                        IMyFaction faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);

                        return new GridGravityData(grid, planetData, faction);
                    }
                }
            }

            return null;
        }

        public GridGravityData(IMyCubeGrid grid, PlanetManager.GravityPlanetData planetData, IMyFaction faction)
        {
            this.grid = grid;
            this.nearestPlanetData = planetData;
            this.planetPos = planetData.planet.PositionComp.GetPosition();
            this.faction = faction;

            SetRelation();
        }

        public void UpdateFromString(string str)
        {
            string[] tokens = str.Split(';');
            if (tokens.Length == 3)
            {
                long gridId, planetId, factionId;
                if (long.TryParse(tokens[0], out gridId) && long.TryParse(tokens[1], out planetId) && PlanetManager.Instance.planetList.ContainsKey(planetId) && long.TryParse(tokens[2], out factionId))
                {
                    IMyCubeGrid grid = MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;
                    if (grid != null)
                    {
                        nearestPlanetData = PlanetManager.Instance.planetList[planetId];
                        faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
                    }
                }
            }
        }

        public override string ToString()
        {
            return grid.EntityId + ";" + nearestPlanetData.planet.EntityId + ";" + (faction != null ? faction.FactionId : 0);
        }

        public static void UpdateFlag()
        {
            updateCtr = (updateCtr + 1) % UPDATE_PERIOD;
            updateFlag = (updateCtr == 0);
        }

        public void SetPlanet(PlanetManager.GravityPlanetData planetData)
        {
            this.nearestPlanetData = planetData;
            planetPos = planetData.planet.PositionComp.GetPosition();
        }

        public void UpdateFaction()
        {
            faction = null;

            if (grid.BigOwners.Count > 0)
            {
                Dictionary<string, int> factionBlockCt = new Dictionary<string, int>();
                List<IMySlimBlock> blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks, (IMySlimBlock block) => { return block.FatBlock is IMyTerminalBlock; });

                string tag;
                foreach (var block in blocks)
                {
                    tag = block.FatBlock.GetOwnerFactionTag();
                    if (tag.Equals("")) continue;

                    if (!factionBlockCt.ContainsKey(tag))
                        factionBlockCt.Add(tag, 1);
                    else
                        factionBlockCt[tag] += 1;
                }

                int maxCt = 0;
                tag = null;
                foreach (var factionPair in factionBlockCt)
                {
                    if (factionPair.Value > maxCt)
                    {
                        tag = factionPair.Key;
                        maxCt = factionPair.Value;
                    }
                }

                if (tag != null)
                {
                    faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(tag);
                }
            }
        }

        public void SetRelation()
        {
            IMyPlayer player = MyAPIGateway.Session.Player;
            if (player == null) return;

            IMyFaction playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);

            noShow = false;
            if (faction != null)
            {
                if (faction == playerFaction)
                {
                    relationColor = RealisticGravityCore.ColorGridOrbitOrbitFriendly;
                }
                else
                {
                    int rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(player.IdentityId, faction.FactionId);
                    if (rep >= 500)
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitFriendly;
                    }
                    else if (rep <= -500)
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitEnemy;
                        noShow = !RealisticGravityCore.ConfigData.ShowGridOrbitPathEnemy;
                    }
                    else
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitNeutral;
                    }
                }
            }
            else
            {
                if (grid.BigOwners.Count > 0)
                {
                    if (grid.BigOwners[0] != MyAPIGateway.Session.Player.IdentityId)
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitEnemy;
                        noShow = !RealisticGravityCore.ConfigData.ShowGridOrbitPathEnemy;
                    }
                    else
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitFriendly;
                    }
                }
                else
                {
                    relationColor = RealisticGravityCore.ColorGridOrbitOrbitNeutral;
                }
            }
        }

        public bool Update(ref Vector3 camPos, ref IMyControllableEntity controlledEntity, int showPathsToggle, ref GridGravityDataClient clientData)
        {
            if (grid == null || grid.Physics == null || grid.IsStatic || nearestPlanetData == null)
            {
                RealisticGravityCore.UnsetGPS(ref gps);
                return false;
            }

            Vector3 r = grid.Physics.CenterOfMassWorld - nearestPlanetData.planet.PositionComp.GetPosition(), v = grid.Physics.LinearVelocity;
            float vSqr = v.LengthSquared();

            if (MyAPIGateway.Session.IsServer)
            {
                grid.Physics.Gravity = Vector3.Normalize(-r) * (float)(nearestPlanetData.gravityConst / r.LengthSquared());
            }

            clientData.gridName = grid.CustomName;
            clientData.gridId = grid.EntityId;
            clientData.planetId = nearestPlanetData.planet.EntityId;
            clientData.factionId = faction != null ? faction.FactionId : 0;
            clientData.bigOwnerId = (grid.BigOwners.Count > 0) ? grid.BigOwners[0] : 0;
            clientData.position = grid.WorldAABB.Center;
            clientData.velocity = grid.Physics.LinearVelocity;

            // DS
            if (MyAPIGateway.Utilities.IsDedicated) return true;

            bool showPath = showPathsToggle != RealisticGravityCore.HIDE_ORBIT_PATHS && !noShow && ((camPos - planetPos).Length() - nearestPlanetData.gravityHeightMax) < RealisticGravityCore.ConfigData.GridOrbitPathMaxDrawDistance && vSqr > 625F;

            if (RealisticGravityCore.ConfigData.ShowGridOrbitGps && showPath)
            {
                RealisticGravityCore.SetGPS(ref gps, grid.CustomName, grid.WorldAABB.Center, relationColor);
            }
            else
            {
                RealisticGravityCore.UnsetGPS(ref gps);
            }

            if (RealisticGravityCore.ConfigData.ShowGridOrbitPath)
            {
                bool isControlled = (controlledEntity is IMyCubeBlock) && grid == (controlledEntity as IMyCubeBlock).CubeGrid;
                
                if (isControlled || updateFlag)
                {
                    float mu = (float)nearestPlanetData.gravityConst;
                    Vector3 planeNormal = Vector3.Cross(r, v);
                    Vector3 e = Vector3.Cross(v, planeNormal) / mu - Vector3.Normalize(r);
                    float eLength = e.Length();
                    Vector3 eNorm = e / eLength;

                    float semimajorAxis = -mu / (vSqr - (2 * mu / r.Length()));
                    float semiminorAxis = (float)Math.Sqrt(semimajorAxis * semimajorAxis * (1 - e.LengthSquared()));
                    Vector3 center = nearestPlanetData.planet.PositionComp.GetPosition() - (e * semimajorAxis), lVect = eNorm * semimajorAxis, sVect = Vector3.Normalize(Vector3.Cross(lVect, planeNormal)) * semiminorAxis;
                    
                    highDetailScaleFactor = (float)Math.Pow(Math.Sqrt(semimajorAxis * semiminorAxis), 0.707) * 4f;

                    clientData.center = center;
                    clientData.lVect = lVect;
                    clientData.sVect = sVect;
                    clientData.eVect = e;
                    clientData.planeNormal = Vector3D.Normalize(planeNormal);
                    clientData.highDetailScaleFactor = highDetailScaleFactor;

                    planeNormal.Normalize();

                    angleOffset = 0f;
                    float camDistSqr = float.MaxValue;
                    if (showPath && showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL)
                    {
                        var camOffset = Vector3D.Normalize(camPos - center);

                        angleOffset = (float)Math.Atan2(camOffset.X * eNorm.Y * planeNormal.Z + eNorm.X * planeNormal.Y * camOffset.Z + planeNormal.X * camOffset.Y * eNorm.Z - camOffset.Z * eNorm.Y * planeNormal.X - eNorm.Z * planeNormal.Y * camOffset.X - planeNormal.Z * camOffset.Y * eNorm.X, Vector3D.Dot(camOffset, eNorm));

                        angleOffset = (float)Math.Atan2(semimajorAxis * Math.Sin(angleOffset), semiminorAxis * Math.Cos(angleOffset));

                        if (angleOffset < 0f) angleOffset += (float)Math.PI * 2;

                        var camNearPt = center + (float)Math.Sin(angleOffset) * sVect + (float)Math.Cos(angleOffset) * lVect;

                        //MathHelpers.DrawSphere(MatrixD.CreateTranslation(camNearPt), (float)(camPos - camNearPt).Length() * 0.01f + 20f, Color.Blue);

                        camDistSqr = (float)(camPos - camNearPt).LengthSquared();
                        camDist = (float)Math.Sqrt(camDistSqr);

                        //if (isControlled)
                        //    MyAPIGateway.Utilities.ShowNotification($"DIST: {(int)camDistSqr} : {(int)(angleOffset * (180 / Math.PI))} : {e.Length()} : {semimajorAxis}", 5);

                        if (float.IsNaN(angleOffset))
                        {
                            angleOffset = 0f;
                            camDistSqr = float.MaxValue;
                            camDist = float.MaxValue;
                        }
                    }

                    double weight = eLength * eLength * 0.15;
                    for (int i = 0; i < DIVISIONS; ++i)
                    {
                        double theta = -Math.Sin(angleList[i]) * Math.Sign(Math.Cos(angleList[i])) * weight + angleList[i];
                        orbitAngles[i] = (float)theta;
                        orbitPoints[i] = center + (float)Math.Sin(theta) * sVect + (float)Math.Cos(theta) * lVect;
                    }

                    hasHighDetail = showPath && camDistSqr < nearestPlanetData.minRenderDistSqr;
                    firstHighDetailIndex = -1;
                    secondHighDetailIndex = -1;
                    if (hasHighDetail)
                    {
                        for (int i = 0; i < DIVISIONS - 1; ++i)
                        {
                            if (angleOffset < orbitAngles[i + 1])
                            {
                                firstHighDetailIndex = i;
                                if (angleOffset < (orbitAngles[i] + orbitAngles[i + 1]) * 0.5f)
                                {
                                    firstHighDetailIndex = (i - 1 + DIVISIONS) % DIVISIONS;
                                    secondHighDetailIndex = i;
                                }
                                else
                                {
                                    firstHighDetailIndex = i;
                                    secondHighDetailIndex = (i + 1) % DIVISIONS;
                                }
                                break;
                            }
                        }

                        if (firstHighDetailIndex == -1)
                        {
                            if (angleOffset > (6.28318f + orbitAngles[DIVISIONS - 1]) * 0.5f)
                            {
                                firstHighDetailIndex = DIVISIONS - 1;
                                secondHighDetailIndex = 0;
                            }
                            else
                            {
                                firstHighDetailIndex = DIVISIONS - 2;
                                secondHighDetailIndex = DIVISIONS - 1;
                            }
                        }

                        int thirdHighDetailIndex = (secondHighDetailIndex + 1) % DIVISIONS;
                        orbitHighDetailPoints[0] = orbitPoints[firstHighDetailIndex];

                        float firstAngle = orbitAngles[firstHighDetailIndex], secondAngle = orbitAngles[secondHighDetailIndex], thirdAngle = orbitAngles[thirdHighDetailIndex];
                        if (secondHighDetailIndex == 0)
                            firstAngle -= MathHelpers.PI_2;
                        if (thirdHighDetailIndex == 0)
                            thirdAngle += MathHelpers.PI_2;

                        for (int i = 1; i < DIVISIONS; ++i)
                        {
                            float angle = MathHelpers.MapRange((float)i / DIVISIONS, 0f, 1f, firstAngle, secondAngle);
                            orbitHighDetailPoints[i] = center + (float)Math.Sin(angle) * sVect + (float)Math.Cos(angle) * lVect;
                        }

                        orbitHighDetailPoints[DIVISIONS] = orbitPoints[secondHighDetailIndex];

                        for (int i = 1; i < DIVISIONS; ++i)
                        {
                            float angle = MathHelpers.MapRange((float)i / DIVISIONS, 0f, 1f, secondAngle, thirdAngle);
                            orbitHighDetailPoints[i + DIVISIONS] = center + (float)Math.Sin(angle) * sVect + (float)Math.Cos(angle) * lVect;
                        }

                        orbitHighDetailPoints[DIVISIONS * 2] = orbitPoints[thirdHighDetailIndex];

                        //MyAPIGateway.Utilities.ShowNotification($"DETAIL: {firstHighDetailIndex}/{secondHighDetailIndex}/{thirdHighDetailIndex} {angleOffset} : {orbitAngles[0]} : {orbitAngles[10]} : {orbitAngles[20]}", 5);
                    }

                    for (int i = 0; i < DIVISIONS; ++i)
                    {
                        orbitMidPoints[i] = (orbitPoints[i] + orbitPoints[(i + 1) % DIVISIONS]) * 0.5F;
                    }

                    UpdateLineSegmentVisibility(showPathsToggle);

                    SetRelation();
                }

                if (showPath)
                {
                    Vector4 color = isControlled ? RealisticGravityCore.ColorMyOrbitPath : relationColor;
                    if (showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL)
                    {
                        if (hasHighDetail)
                        {
                            //float scaleFactor = (float)Math.Sqrt(clientData.sVect.Length() * clientData.lVect.Length());
                            var detailLineThickness = (camDist + highDetailScaleFactor) * 0.001F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
                            for (int i = 0; i < DIVISIONS; ++i)
                            {
                                if (i == firstHighDetailIndex)
                                {
                                    for (int j = 0; j < orbitHighDetailPoints.Length - 1; ++j)
                                    {
                                        MySimpleObjectDraw.DrawLine(orbitHighDetailPoints[j], orbitHighDetailPoints[j + 1], weaponLaserId, ref color, detailLineThickness, BlendTypeEnum.PostPP);
                                    }
                                }
                                else if (i != secondHighDetailIndex && orbitPointsFlags[i])
                                {
                                    MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % DIVISIONS], weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                                }
                            }
                            //MyAPIGateway.Utilities.ShowNotification($"SCALE: {highDetailScaleFactor}", 5);
                        }
                        else
                        {
                            for (int i = 0; i < DIVISIONS; ++i)
                            {
                                if (orbitPointsFlags[i])
                                    MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % DIVISIONS], weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < DIVISIONS; ++i)
                        {
                            if (orbitPointsFlags[i])
                                MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % DIVISIONS], weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                        }
                    }
                }
            }
            return true;
        }

        public void UpdateLineSegmentVisibility(int showPathsToggle)
        {
            if (nearestPlanetData != null)
            {
                Vector3 camPos = MyAPIGateway.Session.Camera != null ? MyAPIGateway.Session.Camera.WorldMatrix.Translation : Vector3D.Zero;
                Vector3D planetPos = nearestPlanetData.planet.PositionComp.GetPosition();
                for (int i = 0; i < DIVISIONS; ++i)
                {
                    float dSqr = (orbitMidPoints[i] - camPos).LengthSquared(), dSqrPlanet = (float)(orbitMidPoints[i] - planetPos).LengthSquared();
                    orbitPointsFlags[i] = (showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL || dSqr > nearestPlanetData.minRenderDistSqr) && dSqrPlanet < nearestPlanetData.gravityHeightMaxSqr;
                    orbitPointsLineThicknesses[i] = ((float)Math.Sqrt(dSqr)) * 0.001F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
                }
            }
        }

        public void ClearGps()
        {
            if (gps != null)
            {
                RealisticGravityCore.UnsetGPS(ref gps);
            }
        }
    }

    [ProtoContract(UseProtoMembersOnly = true)]
    public class GridGravityDataClient
    {
        [ProtoMember(1)]
        public string gridName;
        [ProtoMember(2)]
        public long gridId;
        [ProtoMember(3)]
        public long planetId;
        [ProtoMember(4)]
        public long factionId;
        [ProtoMember(5)]
        public long bigOwnerId;
        [ProtoMember(6)]
        public Vector3 position;
        [ProtoMember(7)]
        public Vector3 velocity;
        [ProtoMember(8)]
        public Vector3 center;
        [ProtoMember(9)]
        public Vector3 lVect;
        [ProtoMember(10)]
        public Vector3 sVect;
        [ProtoMember(11)]
        public Vector3 eVect;
        [ProtoMember(12)]
        public Vector3 planeNormal;
        [ProtoMember(13)]
        public float highDetailScaleFactor;

        public void CopyData(ref GridGravityDataClient gridData)
        {
            gridName = gridData.gridName;
            gridId = gridData.gridId;
            planetId = gridData.planetId;
            position = gridData.position;
            velocity = gridData.velocity;
            center = gridData.center;
            lVect = gridData.lVect;
            sVect = gridData.sVect;
            eVect = gridData.eVect;
            planeNormal = gridData.planeNormal;
            factionId = gridData.factionId;
            bigOwnerId = gridData.bigOwnerId;

            vSqr = velocity.LengthSquared();

            if (PlanetManager.Instance.planetList.ContainsKey(planetId))
                nearestPlanetData = PlanetManager.Instance.planetList[planetId];

            faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);

            double weight = eVect.LengthSquared() * 0.15;
            for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
            {
                double theta = -Math.Sin(GridGravityData.angleList[i]) * Math.Sign(Math.Cos(GridGravityData.angleList[i])) * weight + GridGravityData.angleList[i];
                orbitAngles[i] = (float)theta;
                orbitPoints[i] = center + (float)Math.Sin(theta) * sVect + (float)Math.Cos(theta) * lVect;
            }

            for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
            {
                orbitMidPoints[i] = (orbitPoints[i] + orbitPoints[(i + 1) % GridGravityData.DIVISIONS]) * 0.5F;
            }

            var camera = MyAPIGateway.Session.Camera;
            if (camera != null && nearestPlanetData != null)
            {
                UpdateLineSegmentVisibility(RealisticGravityCore.showPathsToggle);
            }
            else
            {
                for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                {
                    orbitPointsFlags[i] = true;
                    orbitPointsLineThicknesses[i] = 10F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
                }
            }

            SetRelation();
        }

        private IMyCubeGrid grid;
        private PlanetManager.GravityPlanetData nearestPlanetData;
        private float vSqr;
        private bool noShow;
        private IMyGps gps;
        private IMyFaction faction;
        private Vector4 relationColor;

        private float camDist = float.MaxValue;
        private bool hasHighDetail;
        private int firstHighDetailIndex = -1;
        private int secondHighDetailIndex = -1;
        private Vector3[] orbitHighDetailPoints = new Vector3[GridGravityData.DIVISIONS * 2 + 1];

        private float[] orbitAngles = new float[GridGravityData.DIVISIONS];
        private Vector3[] orbitPoints = new Vector3[GridGravityData.DIVISIONS];
        private Vector3[] orbitMidPoints = new Vector3[GridGravityData.DIVISIONS];
        private bool[] orbitPointsFlags = new bool[GridGravityData.DIVISIONS];
        private float[] orbitPointsLineThicknesses = new float[GridGravityData.DIVISIONS];

        private void SetRelation()
        {
            IMyPlayer player = MyAPIGateway.Session.Player;
            IMyFaction playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(player.IdentityId);

            noShow = false;
            if (faction != null)
            {
                if (faction == playerFaction)
                {
                    relationColor = RealisticGravityCore.ColorGridOrbitOrbitFriendly;
                }
                else
                {
                    int rep = MyAPIGateway.Session.Factions.GetReputationBetweenPlayerAndFaction(player.IdentityId, faction.FactionId);
                    if (rep >= 500)
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitFriendly;
                    }
                    else if (rep <= -500)
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitEnemy;
                        noShow = !RealisticGravityCore.ConfigData.ShowGridOrbitPathEnemy;
                    }
                    else
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitNeutral;
                    }
                }
            }
            else
            {
                if (bigOwnerId != 0)
                {
                    if (bigOwnerId != MyAPIGateway.Session.Player.IdentityId)
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitEnemy;
                        noShow = !RealisticGravityCore.ConfigData.ShowGridOrbitPathEnemy;
                    }
                    else
                    {
                        relationColor = RealisticGravityCore.ColorGridOrbitOrbitFriendly;
                    }
                }
                else
                {
                    relationColor = RealisticGravityCore.ColorGridOrbitOrbitNeutral;
                }
            }
        }

        public void Update(ref Vector3 camPos, ref IMyControllableEntity controlledEntity, int showPathsToggle)
        {
            if (nearestPlanetData == null)
            {
                RealisticGravityCore.UnsetGPS(ref gps);
                return;
            }

            grid = MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;

            if (grid != null && grid.Physics != null)
            {
                position = grid.Physics.CenterOfMassWorld;
            }
            else
            {
                position += velocity * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            }
            
            bool showPath = showPathsToggle != RealisticGravityCore.HIDE_ORBIT_PATHS && !noShow && ((camPos - nearestPlanetData.planet.PositionComp.GetPosition()).Length() - nearestPlanetData.gravityHeightMax) < RealisticGravityCore.ConfigData.GridOrbitPathMaxDrawDistance;
            
            if (RealisticGravityCore.ConfigData.ShowGridOrbitGps && showPath)
            {
                RealisticGravityCore.SetGPS(ref gps, gridName, position, relationColor);
            }
            else
            {
                RealisticGravityCore.UnsetGPS(ref gps);
            }
            
            bool isControlled = grid != null && grid.Physics != null && (controlledEntity is IMyCubeBlock) && grid == (controlledEntity as IMyCubeBlock).CubeGrid;

            if (RealisticGravityCore.ConfigData.ShowGridOrbitPath && showPath)
            {
                if (isControlled)
                {
                    Vector3 gridPos = grid.Physics.CenterOfMassWorld;
                    Vector3 r = gridPos - nearestPlanetData.planet.PositionComp.GetPosition();
                    Vector3 v = grid.Physics.LinearVelocity;
                    float mu = (float)nearestPlanetData.gravityConst;
                    Vector3 planeNormal = Vector3.Cross(r, v);
                    Vector3 e = Vector3.Cross(v, planeNormal) / mu - Vector3.Normalize(r);

                    float semimajorAxis = -mu / (v.LengthSquared() - (2 * mu / r.Length())), semiminorAxis = (float)Math.Sqrt(semimajorAxis * semimajorAxis * (1 - e.LengthSquared()));
                    Vector3 center = nearestPlanetData.planet.PositionComp.GetPosition() - (e * semimajorAxis), lVect = Vector3.Normalize(e) * semimajorAxis, sVect = Vector3.Normalize(Vector3.Cross(lVect, planeNormal)) * semiminorAxis;

                    double weight = e.LengthSquared() * 0.15;
                    for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                    {
                        //double theta = (float)GridGravityData.angleList[i];
                        double theta = -Math.Sin(GridGravityData.angleList[i]) * Math.Sign(Math.Cos(GridGravityData.angleList[i])) * weight + GridGravityData.angleList[i];
                        orbitAngles[i] = (float)theta;
                        orbitPoints[i] = center + (float)Math.Sin(theta) * sVect + (float)Math.Cos(theta) * lVect;
                    }

                    grid.Physics.Gravity = Vector3.Normalize(-r) * (float)(nearestPlanetData.gravityConst / r.LengthSquared());

                    eVect = e;
                    this.planeNormal = Vector3D.Normalize(planeNormal);
                    this.center = center;
                    this.lVect = lVect;
                    this.sVect = sVect;
                    this.highDetailScaleFactor = (float)Math.Pow(Math.Sqrt(semimajorAxis * semiminorAxis), 0.707) * 4f;
                    UpdateLineSegmentVisibility(showPathsToggle);
                }

                Vector4 color = isControlled ? RealisticGravityCore.ColorMyOrbitPath : relationColor;
                if (showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL)
                {
                    if (hasHighDetail)
                    {
                        var detailLineThickness = (camDist + highDetailScaleFactor) * 0.001F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
                        for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                        {
                            if (i == firstHighDetailIndex)
                            {
                                for (int j = 0; j < orbitHighDetailPoints.Length - 1; ++j)
                                {
                                    MySimpleObjectDraw.DrawLine(orbitHighDetailPoints[j], orbitHighDetailPoints[j + 1], GridGravityData.weaponLaserId, ref color, detailLineThickness, BlendTypeEnum.PostPP);
                                }
                            }
                            else if (i != secondHighDetailIndex && orbitPointsFlags[i])
                            {
                                MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % GridGravityData.DIVISIONS], GridGravityData.weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                            }
                        }
                    }
                    else
                    {
                        for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                        {
                            if (orbitPointsFlags[i])
                                MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % GridGravityData.DIVISIONS], GridGravityData.weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                    {
                        if (orbitPointsFlags[i])
                            MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % GridGravityData.DIVISIONS], GridGravityData.weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                    }
                }
            }
        }

        public void UpdateLineSegmentVisibility(int showPathsToggle)
        {
            if (MyAPIGateway.Session.Camera != null && nearestPlanetData != null)
            {
                Vector3 camPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                Vector3 planetPos = nearestPlanetData.planet.PositionComp.GetPosition();
                for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                {
                    float dSqr = (orbitMidPoints[i] - camPos).LengthSquared(), dSqrPlanet = (orbitMidPoints[i] - planetPos).LengthSquared();
                    orbitPointsFlags[i] = (showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL || dSqr > nearestPlanetData.minRenderDistSqr) && dSqrPlanet < nearestPlanetData.gravityHeightMaxSqr;
                    orbitPointsLineThicknesses[i] = ((float)Math.Sqrt(dSqr)) * 0.001F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
                }

                bool showPath = showPathsToggle != RealisticGravityCore.HIDE_ORBIT_PATHS && !noShow && ((camPos - planetPos).Length() - nearestPlanetData.gravityHeightMax) < RealisticGravityCore.ConfigData.GridOrbitPathMaxDrawDistance && vSqr > 625F;
                float angleOffset = 0f;
                float camDistSqr = float.MaxValue;
                if (showPath && showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL)
                {
                    var camOffset = Vector3D.Normalize(camPos - center);
                    float semimajorAxis = (float)lVect.Length(), semiminorAxis = (float)sVect.Length();
                    Vector3D eNorm = Vector3D.Normalize(eVect);

                    angleOffset = (float)Math.Atan2(camOffset.X * eNorm.Y * planeNormal.Z + eNorm.X * planeNormal.Y * camOffset.Z + planeNormal.X * camOffset.Y * eNorm.Z - camOffset.Z * eNorm.Y * planeNormal.X - eNorm.Z * planeNormal.Y * camOffset.X - planeNormal.Z * camOffset.Y * eNorm.X, Vector3D.Dot(camOffset, eNorm));

                    angleOffset = (float)Math.Atan2(semimajorAxis * Math.Sin(angleOffset), semiminorAxis * Math.Cos(angleOffset));

                    if (angleOffset < 0f) angleOffset += (float)Math.PI * 2;

                    var camNearPt = center + (float)Math.Sin(angleOffset) * sVect + (float)Math.Cos(angleOffset) * lVect;

                    //MathHelpers.DrawSphere(MatrixD.CreateTranslation(camNearPt), (float)(camPos - camNearPt).Length() * 0.01f + 20f, Color.Blue);

                    camDistSqr = (float)(camPos - camNearPt).LengthSquared();
                    camDist = (float)Math.Sqrt(camDistSqr);

                    //MyAPIGateway.Utilities.ShowNotification($"DIST: {(int)camDistSqr} : {(int)(angleOffset * (180 / Math.PI))} : {eVect.Length()} : {semimajorAxis}", 5);

                    if (float.IsNaN(angleOffset))
                    {
                        angleOffset = 0f;
                        camDistSqr = float.MaxValue;
                        camDist = float.MaxValue;
                    }
                }

                hasHighDetail = showPath && camDistSqr < nearestPlanetData.minRenderDistSqr;
                firstHighDetailIndex = -1;
                secondHighDetailIndex = -1;
                if (hasHighDetail)
                {
                    for (int i = 0; i < GridGravityData.DIVISIONS - 1; ++i)
                    {
                        if (angleOffset < orbitAngles[i + 1])
                        {
                            firstHighDetailIndex = i;
                            if (angleOffset < (orbitAngles[i] + orbitAngles[i + 1]) * 0.5f)
                            {
                                firstHighDetailIndex = (i - 1 + GridGravityData.DIVISIONS) % GridGravityData.DIVISIONS;
                                secondHighDetailIndex = i;
                            }
                            else
                            {
                                firstHighDetailIndex = i;
                                secondHighDetailIndex = (i + 1) % GridGravityData.DIVISIONS;
                            }
                            break;
                        }
                    }

                    if (firstHighDetailIndex == -1)
                    {
                        if (angleOffset > (6.28318f + orbitAngles[GridGravityData.DIVISIONS - 1]) * 0.5f)
                        {
                            firstHighDetailIndex = GridGravityData.DIVISIONS - 1;
                            secondHighDetailIndex = 0;
                        }
                        else
                        {
                            firstHighDetailIndex = GridGravityData.DIVISIONS - 2;
                            secondHighDetailIndex = GridGravityData.DIVISIONS - 1;
                        }
                    }

                    int thirdHighDetailIndex = (secondHighDetailIndex + 1) % GridGravityData.DIVISIONS;
                    orbitHighDetailPoints[0] = orbitPoints[firstHighDetailIndex];

                    float firstAngle = orbitAngles[firstHighDetailIndex], secondAngle = orbitAngles[secondHighDetailIndex], thirdAngle = orbitAngles[thirdHighDetailIndex];
                    if (secondHighDetailIndex == 0)
                        firstAngle -= MathHelpers.PI_2;
                    if (thirdHighDetailIndex == 0)
                        thirdAngle += MathHelpers.PI_2;

                    for (int i = 1; i < GridGravityData.DIVISIONS; ++i)
                    {
                        float angle = MathHelpers.MapRange((float)i / GridGravityData.DIVISIONS, 0f, 1f, firstAngle, secondAngle);
                        orbitHighDetailPoints[i] = center + (float)Math.Sin(angle) * sVect + (float)Math.Cos(angle) * lVect;
                    }

                    orbitHighDetailPoints[GridGravityData.DIVISIONS] = orbitPoints[secondHighDetailIndex];

                    for (int i = 1; i < GridGravityData.DIVISIONS; ++i)
                    {
                        float angle = MathHelpers.MapRange((float)i / GridGravityData.DIVISIONS, 0f, 1f, secondAngle, thirdAngle);
                        orbitHighDetailPoints[i + GridGravityData.DIVISIONS] = center + (float)Math.Sin(angle) * sVect + (float)Math.Cos(angle) * lVect;
                    }

                    orbitHighDetailPoints[GridGravityData.DIVISIONS * 2] = orbitPoints[thirdHighDetailIndex];

                    //MyAPIGateway.Utilities.ShowNotification($"DETAIL: {firstHighDetailIndex}/{secondHighDetailIndex}/{thirdHighDetailIndex} {angleOffset} : {orbitAngles[0]} : {orbitAngles[10]} : {orbitAngles[20]}", 5);
                }
            }
        }

        public void ClearGPS()
        {
            if (gps != null)
            {
                RealisticGravityCore.UnsetGPS(ref gps);
            }
        }
    }
}
