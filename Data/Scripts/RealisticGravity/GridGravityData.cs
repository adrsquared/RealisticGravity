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
        public Vector3 prevLinearVelocity;
        public IMyFaction faction;
        private Vector4 relationColor;
        private bool noShow;
        private IMyGps gps;

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
            prevLinearVelocity = grid.Physics != null ? grid.Physics.LinearVelocity : Vector3.Zero;

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
            prevLinearVelocity = grid.Physics != null ? grid.Physics.LinearVelocity : Vector3.Zero;
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

        public bool Update(ref Vector3 playerPos, ref IMyControllableEntity controlledEntity, int showPathsToggle, ref GridGravityDataClient clientData)
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
                
                if (RealisticGravityCore.ConfigData.PreventGridStopping && grid.Physics.LinearVelocity.LengthSquared() <= 0.01 && prevLinearVelocity.LengthSquared() > 25.0)
                {
                    grid.Physics.LinearVelocity = prevLinearVelocity;
                }

                prevLinearVelocity = grid.Physics.LinearVelocity;
            }

            clientData.gridName = grid.CustomName;
            clientData.gridId = grid.EntityId;
            clientData.planetId = nearestPlanetData.planet.EntityId;
            clientData.factionId = faction != null ? faction.FactionId : 0;
            clientData.bigOwnerId = (grid.BigOwners.Count > 0) ? grid.BigOwners[0] : 0;
            clientData.position = grid.WorldAABB.Center;
            clientData.velocity = grid.Physics.LinearVelocity;

            // DS
            if (MyAPIGateway.Session.Player == null) return true;

            bool showPath = showPathsToggle != RealisticGravityCore.HIDE_ORBIT_PATHS && !noShow && (playerPos - planetPos).LengthSquared() < RealisticGravityCore.GridOrbitPathMaxDrawDistanceSqr && vSqr > 625F;

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

                    float semimajorAxis = -mu / (vSqr - (2 * mu / r.Length()));
                    Vector3 center = nearestPlanetData.planet.PositionComp.GetPosition() - (e * semimajorAxis), lVect = Vector3.Normalize(e) * semimajorAxis, sVect = Vector3.Normalize(Vector3.Cross(lVect, planeNormal)) * (float)Math.Sqrt(semimajorAxis * semimajorAxis * (1 - e.LengthSquared()));

                    clientData.center = center;
                    clientData.lVect = lVect;
                    clientData.sVect = sVect;

                    double weight = eLength * eLength * 0.15;
                    for (int i = 0; i < DIVISIONS; ++i)
                    {
                        double theta = -Math.Sin(angleList[i]) * Math.Sign(Math.Cos(angleList[i])) * weight + angleList[i];
                        orbitPoints[i] = center + (float)Math.Sin(theta) * sVect + (float)Math.Cos(theta) * lVect;
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
                    for (int i = 0; i < DIVISIONS; ++i)
                    {
                        if (orbitPointsFlags[i])
                            MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % DIVISIONS], weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                    }
                }
            }
            return true;
        }

        public void UpdateLineSegmentVisibility(int showPathsToggle)
        {
            if (nearestPlanetData != null)
            {
                Vector3 charPos = MyAPIGateway.Session.Player.Character != null ? MyAPIGateway.Session.Player.Character.GetPosition() : Vector3D.Zero;
                Vector3D planetPos = nearestPlanetData.planet.PositionComp.GetPosition();
                for (int i = 0; i < DIVISIONS; ++i)
                {
                    float dSqr = (orbitMidPoints[i] - charPos).LengthSquared(), dSqrPlanet = (float)(orbitMidPoints[i] - planetPos).LengthSquared();
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
            factionId = gridData.factionId;
            bigOwnerId = gridData.bigOwnerId;

            vSqr = velocity.LengthSquared();

            if (PlanetManager.Instance.planetList.ContainsKey(planetId))
                nearestPlanetData = PlanetManager.Instance.planetList[planetId];

            faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);

            for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
            {
                orbitPoints[i] = center + (float)Math.Sin(GridGravityData.angleList[i]) * sVect + (float)Math.Cos(GridGravityData.angleList[i]) * lVect;
            }

            for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
            {
                orbitMidPoints[i] = (orbitPoints[i] + orbitPoints[(i + 1) % GridGravityData.DIVISIONS]) * 0.5F;
            }

            var character = MyAPIGateway.Session.Player.Character;
            if (character != null && nearestPlanetData != null)
            {
                Vector3 playerPos = character.GetPosition();
                Vector3D planetPos = nearestPlanetData.planet.PositionComp.GetPosition();
                for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                {
                    float dSqr = (orbitMidPoints[i] - playerPos).LengthSquared(), dSqrPlanet = (float)(orbitMidPoints[i] - planetPos).LengthSquared();
                    orbitPointsFlags[i] = (RealisticGravityCore.showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL || dSqr > nearestPlanetData.minRenderDistSqr) && dSqrPlanet < nearestPlanetData.gravityHeightMaxSqr;
                    orbitPointsLineThicknesses[i] = ((float)Math.Sqrt(dSqr)) * 0.001F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
                }
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

        public void Update(ref Vector3 playerPos, ref IMyControllableEntity controlledEntity, int showPathsToggle)
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
            
            bool showPath = showPathsToggle != RealisticGravityCore.HIDE_ORBIT_PATHS && !noShow && (playerPos - nearestPlanetData.planet.PositionComp.GetPosition()).LengthSquared() < RealisticGravityCore.GridOrbitPathMaxDrawDistanceSqr;
            
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

                    float semimajorAxis = -mu / (v.LengthSquared() - (2 * mu / r.Length()));
                    Vector3 center = nearestPlanetData.planet.PositionComp.GetPosition() - (e * semimajorAxis), lVect = Vector3.Normalize(e) * semimajorAxis, sVect = Vector3.Normalize(Vector3.Cross(lVect, planeNormal)) * (float)Math.Sqrt(semimajorAxis * semimajorAxis * (1 - e.LengthSquared()));

                    double weight = e.LengthSquared() * 0.15;
                    for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                    {
                        double theta = -Math.Sin(GridGravityData.angleList[i]) * Math.Sign(Math.Cos(GridGravityData.angleList[i])) * weight + GridGravityData.angleList[i];
                        orbitPoints[i] = center + (float)Math.Sin(theta) * sVect + (float)Math.Cos(theta) * lVect;
                    }

                    UpdateLineSegmentVisibility(showPathsToggle);
                }

                Vector4 color = isControlled ? RealisticGravityCore.ColorMyOrbitPath : relationColor;
                for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
                {
                    if (orbitPointsFlags[i])
                        MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % GridGravityData.DIVISIONS], GridGravityData.weaponLaserId, ref color, orbitPointsLineThicknesses[i], BlendTypeEnum.PostPP);
                }
            }
        }

        public void UpdateLineSegmentVisibility(int showPathsToggle)
        {
            Vector3 charPos = MyAPIGateway.Session.Player.Character != null ? MyAPIGateway.Session.Player.Character.GetPosition() : Vector3D.Zero;
            Vector3 planetPos = nearestPlanetData != null ? nearestPlanetData.planet.PositionComp.GetPosition() : Vector3D.Zero;
            for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
            {
                float dSqr = (orbitMidPoints[i] - charPos).LengthSquared(), dSqrPlanet = (orbitMidPoints[i] - planetPos).LengthSquared();
                orbitPointsFlags[i] = (showPathsToggle == RealisticGravityCore.SHOW_ORBIT_PATHS_FULL || dSqr > nearestPlanetData.minRenderDistSqr) && dSqrPlanet < nearestPlanetData.gravityHeightMaxSqr;
                orbitPointsLineThicknesses[i] = ((float)Math.Sqrt(dSqr)) * 0.001F * RealisticGravityCore.ConfigData.OrbitPathLineThickness;
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
