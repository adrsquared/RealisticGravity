using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.ModAPI;
using Sandbox.Game;
using VRage.Utils;
using VRage.Game;
using System;
using Draygo.API;
using System.Text;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using Sandbox.Definitions;
using Sandbox.Game.Entities;

namespace RealisticGravity
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class RealisticGravityCore : MySessionComponentBase
    {
        public static RealisticGravityCore Instance;

        // Networking
        public readonly Networking Network = new Networking(45151);

        private const string CONFIG_FILE_NAME = "Config.xml";
        public static OrbitSettingsConfig ConfigData;

        public static bool ShowAnyInfo;
        public static bool TrackGrids;
        public static double GridOrbitPathMaxDrawDistanceSqr;
        public static Vector4 ColorMyOrbitPath;
        public static Vector4 ColorGridOrbitOrbitFriendly;
        public static Vector4 ColorGridOrbitOrbitNeutral;
        public static Vector4 ColorGridOrbitOrbitEnemy;
        public static Vector4 ColorPeriApoGps;

        private static readonly MyStringId weaponLaserId = MyStringId.GetOrCompute("WeaponLaser");

        public Dictionary<IMyEntity, GridGravityData> gridDataTable = new Dictionary<IMyEntity, GridGravityData>();
        public Dictionary<long, GridGravityDataClient> gridDataTableClient = new Dictionary<long, GridGravityDataClient>();
        private static HashSet<IMyGps> gpsCachedSet = null;

        public override void LoadData()
        {
            PlanetManager.Create();

            if (MyAPIGateway.Session.IsServer)
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(CONFIG_FILE_NAME, typeof(OrbitSettingsConfig)))
                {
                    MyAPIGateway.Parallel.Start(() =>
                    {
                        using (var sw = MyAPIGateway.Utilities.WriteFileInWorldStorage(CONFIG_FILE_NAME,
                            typeof(OrbitSettingsConfig))) sw.Write(MyAPIGateway.Utilities.SerializeToXML<OrbitSettingsConfig>(new OrbitSettingsConfig()));
                    });
                }

                try
                {
                    ConfigData = null;
                    var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(CONFIG_FILE_NAME, typeof(OrbitSettingsConfig));
                    string configcontents = reader.ReadToEnd();
                    ConfigData = MyAPIGateway.Utilities.SerializeFromXML<OrbitSettingsConfig>(configcontents);

                    byte[] bytes = MyAPIGateway.Utilities.SerializeToBinary(ConfigData);
                    string encodedConfig = Convert.ToBase64String(bytes);

                    MyAPIGateway.Utilities.SetVariable("OrbitSettings_Config_xml", encodedConfig);

                    //MyLog.Default.WriteLineAndConsole($"EXPANDED!: " + encodedConfig);
                }
                catch (Exception exc)
                {
                    ConfigData = new OrbitSettingsConfig();
                    //MyLog.Default.WriteLineAndConsole($"ERROR: {exc.Message} : {exc.StackTrace} : {exc.InnerException}");
                }
            }
            else
            {
                try
                {
                    string str;
                    MyAPIGateway.Utilities.GetVariable("OrbitSettings_Config_xml", out str);

                    byte[] bytes = Convert.FromBase64String(str);
                    ConfigData = MyAPIGateway.Utilities.SerializeFromBinary<OrbitSettingsConfig>(bytes);
                }
                catch
                {
                    ConfigData = new OrbitSettingsConfig();
                }
            }

            if (ConfigData.GlobalMaxSpeedMultiplier_LargeGrid > 0F)
            {
                MyDefinitionManager.Static.EnvironmentDefinition.LargeShipMaxSpeed = 100F * ConfigData.GlobalMaxSpeedMultiplier_LargeGrid;
            }

            if (ConfigData.GlobalMaxSpeedMultiplier_SmallGrid > 0F)
            {
                MyDefinitionManager.Static.EnvironmentDefinition.SmallShipMaxSpeed = 100F * ConfigData.GlobalMaxSpeedMultiplier_SmallGrid;
            }

            if (ConfigData.CharacterGravityMultiplier > 0F)
                MyPerGameSettings.CharacterGravityMultiplier = ConfigData.CharacterGravityMultiplier;

            ShowAnyInfo = ConfigData.ShowCharacterOrbitInfo || ConfigData.ShowGridOrbitInfo;
            TrackGrids = ConfigData.ShowGridOrbitInfo || ConfigData.ShowGridOrbitPath || ConfigData.ShowGridOrbitGps;
            GridOrbitPathMaxDrawDistanceSqr = ConfigData.GridOrbitPathMaxDrawDistance * ConfigData.GridOrbitPathMaxDrawDistance;
            ColorMyOrbitPath = new Vector4(ConfigData.ColorMyOrbitPath.X, ConfigData.ColorMyOrbitPath.Y, ConfigData.ColorMyOrbitPath.Z, 1);
            ColorGridOrbitOrbitFriendly = new Vector4(ConfigData.ColorGridOrbitPathFriendly.X, ConfigData.ColorGridOrbitPathFriendly.Y, ConfigData.ColorGridOrbitPathFriendly.Z, 1);
            ColorGridOrbitOrbitNeutral = new Vector4(ConfigData.ColorGridOrbitPathNeutral.X, ConfigData.ColorGridOrbitPathNeutral.Y, ConfigData.ColorGridOrbitPathNeutral.Z, 1);
            ColorGridOrbitOrbitEnemy = new Vector4(ConfigData.ColorGridOrbitPathEnemy.X, ConfigData.ColorGridOrbitPathEnemy.Y, ConfigData.ColorGridOrbitPathEnemy.Z, 1);
            ColorMyOrbitPath = new Vector4(ConfigData.ColorMyOrbitPath.X, ConfigData.ColorMyOrbitPath.Y, ConfigData.ColorMyOrbitPath.Z, 1);

            GridGravityData.DIVISIONS = ConfigData.OrbitRenderDivisions;
            GridGravityData.angleList = new double[GridGravityData.DIVISIONS];
            for (int i = 0; i < GridGravityData.DIVISIONS; ++i)
            {
                GridGravityData.angleList[i] = i * (2 * Math.PI / GridGravityData.DIVISIONS);
            }

            Network.Register();
        }

        protected override void UnloadData()
        {
            PlanetManager.Destroy();

            Network.Unregister();
        }

        public override void BeforeStart()
        {
            SetUpdateOrder(MyUpdateOrder.AfterSimulation);

            Instance = this;

            MyAPIGateway.Session.SessionSettings.StopGridsPeriodMin = 0;
        }

        private bool initComplete = false;
        private const int UPDATE_GRIDS_LENGTH = 300;
        private int updateGridsCtr = 0;

        public HudAPIv2 HUD;
        private HudAPIv2.HUDMessage element_status;
        private HudAPIv2.HUDMessage element_periapsis;
        private HudAPIv2.HUDMessage element_apoapsis;
        private HudAPIv2.HUDMessage element_extraInfo;
        private IMyGps gpsPeriapsis;
        private IMyGps gpsApoapsis;

        public static bool showInfoToggle = true;
        public static int showPathsToggle = 0;
        public const int SHOW_ORBIT_PATHS_NORMAL = 0;
        public const int SHOW_ORBIT_PATHS_FULL = 1;
        public const int HIDE_ORBIT_PATHS = 2;

        // Display Info Vars
        Vector3 r, planeNormal, e, periPos, apoPos;
        float mu, eLength, semimajorAxis, periLength, periLengthSqr, apoLength, apoLengthSqr;
        double inclination, trueAnomaly, eccentricAnomaly, meanAnomaly, timeOrbitFactor, ttPeriapsis, ttApoapsis;

        public override void UpdateAfterSimulation()
        {
            if (!initComplete)
            {
                if (MyAPIGateway.Session == null)
                    return;

                PlanetManager.Instance.Init();
                HUD = new HudAPIv2();

                initComplete = true;
            }

            if (MyAPIGateway.Session.Player != null)
            {
                if (MyAPIGateway.Input.IsAnyCtrlKeyPressed() && MyAPIGateway.Input.IsNewKeyPressed(VRage.Input.MyKeys.O))
                {
                    showInfoToggle = !showInfoToggle;
                    MyVisualScriptLogicProvider.ShowNotification(showInfoToggle ? "[Show Orbit Info]" : "[Hide Orbit Info]", 1500);
                }

                if (MyAPIGateway.Input.IsAnyShiftKeyPressed() && MyAPIGateway.Input.IsNewKeyPressed(VRage.Input.MyKeys.O))
                {
                    showPathsToggle = (showPathsToggle + 1) % 3;
                    switch (showPathsToggle)
                    {
                        case SHOW_ORBIT_PATHS_NORMAL:
                            MyVisualScriptLogicProvider.ShowNotification("[Show Orbit Paths (Normal)]", 1500);
                            break;
                        case SHOW_ORBIT_PATHS_FULL:
                            MyVisualScriptLogicProvider.ShowNotification("[Show Orbit Paths (Full)]", 1500);
                            break;
                        case HIDE_ORBIT_PATHS:
                            MyVisualScriptLogicProvider.ShowNotification("[Hide Orbit Paths]", 1500);
                            break;
                    }

                    if (showPathsToggle != HIDE_ORBIT_PATHS)
                    {
                        if (MyAPIGateway.Session.IsServer)
                        {
                            foreach (var gridData in gridDataTable.Values)
                                gridData.UpdateLineSegmentVisibility(showPathsToggle);
                        }
                        else
                        {
                            foreach (var gridDataClient in gridDataTableClient.Values)
                                gridDataClient.UpdateLineSegmentVisibility(showPathsToggle);
                        }
                    }
                }

                if (HUD.Heartbeat && element_status == null)
                {
                    element_status = new HudAPIv2.HUDMessage(new StringBuilder(""), new Vector2D(ConfigData.OrbitInfoScreenOffset.X, ConfigData.OrbitInfoScreenOffset.Y), Blend: BlendTypeEnum.PostPP);
                    element_periapsis = new HudAPIv2.HUDMessage(new StringBuilder(""), new Vector2D(ConfigData.OrbitInfoScreenOffset.X, ConfigData.OrbitInfoScreenOffset.Y - 0.04), Blend: BlendTypeEnum.PostPP);
                    element_apoapsis = new HudAPIv2.HUDMessage(new StringBuilder(""), new Vector2D(ConfigData.OrbitInfoScreenOffset.X, ConfigData.OrbitInfoScreenOffset.Y - 0.08), Blend: BlendTypeEnum.PostPP);
                    element_extraInfo = new HudAPIv2.HUDMessage(new StringBuilder(""), new Vector2D(ConfigData.OrbitInfoScreenOffset.X, ConfigData.OrbitInfoScreenOffset.Y - 0.12), Blend: BlendTypeEnum.PostPP);
                }

                if (element_status == null)
                    return;
            }

            if (MyAPIGateway.Session.IsServer)
            {
                // Run Grid List Refresh
                if (updateGridsCtr == 0)
                {
                    if (MyAPIGateway.Session.Player != null)
                    {
                        gpsCachedSet = new HashSet<IMyGps>(MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId));
                    }

                    HashSet<IMyCubeGrid> controlledGrids = new HashSet<IMyCubeGrid>();
                    List<IMyPlayer> players = new List<IMyPlayer>();
                    MyAPIGateway.Players.GetPlayers(players);
                    foreach (var p in players)
                    {
                        if (p.Controller.ControlledEntity is IMyCubeBlock)
                        {
                            controlledGrids.Add((p.Controller.ControlledEntity as IMyCubeBlock).CubeGrid);
                        }
                    }
                    
                    HashSet<IMyEntity> grids = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(grids, (IMyEntity e) => { return e is IMyCubeGrid && !(e as IMyCubeGrid).IsStatic && (e as IMyCubeGrid).Physics != null && ((e as MyCubeGrid).BlocksCount >= 15 || controlledGrids.Contains(e as IMyCubeGrid)); });

                    Dictionary<IMyGridTerminalSystem, IMyCubeGrid> gridTerminalSystemDictionary = new Dictionary<IMyGridTerminalSystem, IMyCubeGrid>();
                    Dictionary<IMyGridTerminalSystem, int> gridTerminalSystemBlockCtDictionary = new Dictionary<IMyGridTerminalSystem, int>();
                    foreach (var grid in grids)
                    {
                        var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid as IMyCubeGrid);
                        if (terminalSystem != null)
                        {
                            if (!gridTerminalSystemDictionary.ContainsKey(terminalSystem))
                            {
                                gridTerminalSystemDictionary.Add(terminalSystem, grid as IMyCubeGrid);
                                gridTerminalSystemBlockCtDictionary.Add(terminalSystem, (grid as MyCubeGrid).BlocksCount * ((grid as IMyCubeGrid).GridSizeEnum == MyCubeSize.Large ? 5 : 1));
                            }
                            else
                            {
                                int ct = (grid as MyCubeGrid).BlocksCount * ((grid as IMyCubeGrid).GridSizeEnum == MyCubeSize.Large ? 5 : 1);
                                if (ct > gridTerminalSystemBlockCtDictionary[terminalSystem])
                                {
                                    gridTerminalSystemDictionary[terminalSystem] = grid as IMyCubeGrid;
                                    gridTerminalSystemBlockCtDictionary[terminalSystem] = ct;
                                }
                            }
                        }
                    }

                    grids = new HashSet<IMyEntity>(gridTerminalSystemDictionary.Values);
                    //MyVisualScriptLogicProvider.ShowNotification($"GRIDS: {grids.Count}", 300);

                    int num;
                    var toRemoveWhitelist = new HashSet<IMyEntity>(gridDataTable.Keys);
                    foreach (var grid in grids)
                    {
                        PlanetManager.GravityPlanetData planetData = PlanetManager.Instance.GetNearestPlanet(grid.PositionComp.GetPosition(), out num);
                        //MyVisualScriptLogicProvider.ShowNotification($"PLANET: {num}", 3000);
                        if (planetData != null && (num == 1 || ConfigData.EnforceSingleGravityWell))
                        {
                            //MyVisualScriptLogicProvider.ShowNotification($"PLANET: {(grid as IMyCubeGrid).CustomName} : {planet}", 300);
                            if (gridDataTable.ContainsKey(grid))
                            {
                                gridDataTable[grid].SetPlanet(planetData);
                                gridDataTable[grid].UpdateFaction();
                                gridDataTable[grid].SetRelation();
                                toRemoveWhitelist.Remove(grid);
                            }
                            else
                            {
                                gridDataTable.Add(grid, new GridGravityData(grid as IMyCubeGrid, planetData));
                                gridDataTableClient.Add(grid.EntityId, new GridGravityDataClient());
                            }
                        }
                    }

                    foreach (var remove in toRemoveWhitelist)
                    {
                        gridDataTable[remove].ClearGps();
                        gridDataTable.Remove(remove);
                        gridDataTableClient.Remove(remove.EntityId);
                    }
                }
            }
            else
            {
                if (updateGridsCtr == 0 && MyAPIGateway.Session.Player != null)
                {
                    gpsCachedSet = new HashSet<IMyGps>(MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId));
                }
            }

            updateGridsCtr = (updateGridsCtr + 1) % UPDATE_GRIDS_LENGTH;

            IMyPlayer player = MyAPIGateway.Session.Player;
            var controlledEntity = player != null ? player.Controller.ControlledEntity : null;
            Vector3 pos = Vector3.Zero, v = Vector3.Zero;
            if (player != null)
            {
                if (player.Character != null)
                {
                    pos = player.Character.WorldAABB.Center;
                }
                bool isValid = false;
                bool showInfo = false;
                if (controlledEntity != null)
                {
                    if (controlledEntity is IMyCharacter && (!(controlledEntity as IMyCharacter).EnabledThrusts || !(controlledEntity as IMyCharacter).EnabledDamping))
                    {
                        pos = player.Character.WorldAABB.Center;
                        v = player.Character.Physics.LinearVelocity;
                        showInfo = ConfigData.ShowCharacterOrbitInfo && showInfoToggle;
                        isValid = true;
                    }
                    else if (controlledEntity is IMyCubeBlock)
                    {
                        var grid = (controlledEntity as IMyCubeBlock).CubeGrid;
                        if (!grid.IsStatic && grid.Physics != null)
                        {
                            pos = (controlledEntity as IMyCubeBlock).CubeGrid.Physics.CenterOfMassWorld;
                            v = (controlledEntity as IMyCubeBlock).CubeGrid.Physics.LinearVelocity;
                            showInfo = ConfigData.ShowGridOrbitInfo && showInfoToggle;
                            isValid = true;
                        }
                    }

                    element_status.Visible = true;
                    element_periapsis.Visible = true;
                    element_apoapsis.Visible = true;
                    element_extraInfo.Visible = true;

                    element_status.Message.Clear();
                    element_periapsis.Message.Clear();
                    element_apoapsis.Message.Clear();
                    element_extraInfo.Message.Clear();

                    int numPlanets;
                    var planet = PlanetManager.Instance.GetNearestPlanet(pos, out numPlanets);
                    //MyVisualScriptLogicProvider.ShowNotification($"{player.Controller.ControlledEntity} : {v.Length()} : {PlanetManager.Instance.planetList.Count} : {numPlanets}", 5);
                    if (!ConfigData.EnforceSingleGravityWell && numPlanets > 1)
                    {
                        if (showInfo)
                        {
                            element_status.Message.Append("Multiple Gravity Fields");
                            element_periapsis.Message.Append($"Periapsis: N/A");
                            element_apoapsis.Message.Append($"Apoapsis: N/A");
                            element_extraInfo.Message.Append($"e: N/A   i: N/A   T: N/A");
                        }

                        UnsetGPS(ref gpsPeriapsis);
                        UnsetGPS(ref gpsApoapsis);
                    }
                    else if (isValid && planet != null && v.LengthSquared() > 625F)
                    {
                        r = pos - planet.planet.PositionComp.GetPosition();
                        mu = (float)planet.gravityConst * (controlledEntity is IMyCharacter ? MyPerGameSettings.CharacterGravityMultiplier : 1);
                        planeNormal = Vector3.Cross(r, v);
                        e = Vector3.Cross(v, planeNormal) / mu - Vector3.Normalize(r);
                        eLength = e.Length();
                        inclination = MathHelper.ToDegrees(Math.Acos(Vector3.Dot(Vector3.Normalize(Vector3.Cross(v, r)), Vector3.Up)));
                        if (inclination > 90) inclination = 180F - inclination;

                        semimajorAxis = -mu / (v.LengthSquared() - (2 * mu / r.Length()));
                        periLength = semimajorAxis * (1 - eLength);
                        apoLength = semimajorAxis * (1 + eLength);
                        periLengthSqr = periLength * periLength;
                        apoLengthSqr = apoLength * apoLength;
                        periPos = planet.planet.PositionComp.GetPosition() + (e / eLength) * periLength;
                        apoPos = planet.planet.PositionComp.GetPosition() - (e / eLength) * apoLength;

                        trueAnomaly = Math.Acos(Vector3.Dot(Vector3.Normalize(e), Vector3.Normalize(r)));
                        if (Vector3.Dot(v, e) > 0F) trueAnomaly = 2 * Math.PI - trueAnomaly;

                        eccentricAnomaly = 2 * Math.Atan2(Math.Tan(trueAnomaly / 2), Math.Sqrt((1 + eLength) / (1 - eLength)));
                        meanAnomaly = eccentricAnomaly - eLength * Math.Sin(eccentricAnomaly);
                        if (meanAnomaly < 0) meanAnomaly = 2 * Math.PI + meanAnomaly;

                        timeOrbitFactor = Math.Sqrt(semimajorAxis * semimajorAxis * semimajorAxis / mu);
                        ttPeriapsis = timeOrbitFactor * (2 * Math.PI - meanAnomaly);
                        ttApoapsis = timeOrbitFactor * ((meanAnomaly > Math.PI ? (3 * Math.PI) : Math.PI) - meanAnomaly);

                        // Show Info
                        if (showInfo)
                        {
                            DrawInfo(ref planet);
                        }

                        // Show Peri/Apoapsis GPS
                        if (ConfigData.ShowPeriApoGps && (!(controlledEntity is IMyCharacter) || ConfigData.ShowCharacterOrbitPath))
                        {
                            if (!double.IsNaN(ttPeriapsis) && periLengthSqr > planet.gravityHeightSurfSqr)
                            {
                                if (((apoLength / periLength) - 1) > 0.02)
                                    SetGPS(ref gpsPeriapsis, "Periapsis", periPos, ConfigData.ColorPeriApoGps);
                                else
                                    UnsetGPS(ref gpsPeriapsis);

                            }
                            else
                            {
                                UnsetGPS(ref gpsPeriapsis);
                            }

                            if (!double.IsNaN(ttApoapsis) && apoLengthSqr < planet.gravityHeightMaxSqr)
                            {
                                if (((apoLength / periLength) - 1) > 0.02)
                                    SetGPS(ref gpsApoapsis, "Apoapsis", apoPos, ConfigData.ColorPeriApoGps);
                                else
                                    UnsetGPS(ref gpsApoapsis);
                            }
                            else
                            {
                                UnsetGPS(ref gpsApoapsis);
                            }
                        }

                        // Show Character Path
                        if (showPathsToggle != HIDE_ORBIT_PATHS && ConfigData.ShowCharacterOrbitPath && controlledEntity is IMyCharacter)
                        {
                            Vector3 center = (apoPos + periPos) * 0.5F, lVect = Vector3.Normalize(e) * semimajorAxis, sVect = Vector3.Normalize(Vector3.Cross(lVect, planeNormal)) * (float)Math.Sqrt(semimajorAxis * semimajorAxis * (1 - e.LengthSquared()));
                            List<Vector3> orbitPoints = new List<Vector3>();
                            for (int i = 0; i < ConfigData.OrbitRenderDivisions; ++i)
                            {
                                double theta = i * (2 * Math.PI / ConfigData.OrbitRenderDivisions) - eccentricAnomaly;
                                orbitPoints.Add(center + (float)Math.Sin(theta) * sVect + (float)Math.Cos(theta) * lVect);
                            }

                            int offset = Math.Min(ConfigData.OrbitRenderDivisions * 3 / 20, 1);
                            for (int i = offset; i < orbitPoints.Count - offset; ++i)
                            {
                                MySimpleObjectDraw.DrawLine(orbitPoints[i], orbitPoints[(i + 1) % orbitPoints.Count], weaponLaserId, ref ColorMyOrbitPath, 100F * (float)Math.Sin(i * (Math.PI / ConfigData.OrbitRenderDivisions)) * ConfigData.OrbitPathLineThickness, BlendTypeEnum.PostPP);
                            }
                        }
                    }
                    else
                    {
                        element_status.Visible = false;
                        element_periapsis.Visible = false;
                        element_apoapsis.Visible = false;
                        element_extraInfo.Visible = false;
                        UnsetGPS(ref gpsPeriapsis);
                        UnsetGPS(ref gpsApoapsis);
                    }
                }
            }

            if (MyAPIGateway.Session.IsServer)
            {
                // Run Main Server Update
                GridGravityData.UpdateFlag();
                Vector3 camPos = (MyAPIGateway.Session.Camera != null ? (Vector3)MyAPIGateway.Session.Camera.WorldMatrix.Translation : pos);
                foreach (var gridData in gridDataTable.Values)
                {
                    var clientData = gridDataTableClient[gridData.grid.EntityId];
                    gridData.Update(ref camPos, ref controlledEntity, showPathsToggle, ref clientData);
                }
                //MyAPIGateway.Utilities.ShowNotification($"ZZZ: {gridDataTable.Count}", 5);

                if (GridGravityData.updateFlag)
                {
                    Network.SendGameState();
                }
            }
            else if (TrackGrids)
            {
                // Run Main Client Update
                Vector3 camPos = MyAPIGateway.Session.Camera != null ? pos : (Vector3)MyAPIGateway.Session.Camera.WorldMatrix.Translation;
                foreach (var gridDataClient in gridDataTableClient.Values)
                    gridDataClient.Update(ref camPos, ref controlledEntity, showPathsToggle);
            }
        }

        public static void SetGPS(ref IMyGps gps, string name, Vector3 pos, Color color)
        {
            if (gpsCachedSet != null)
            {
                if (gps == null || !gpsCachedSet.Contains(gps))
                {
                    if (gps != null)
                        MyAPIGateway.Session.GPS.RemoveLocalGps(gps);

                    gps = MyAPIGateway.Session.GPS.Create(name, "", pos, true);
                    gps.GPSColor = color;
                    MyAPIGateway.Session.GPS.AddLocalGps(gps);
                    gpsCachedSet.Add(gps);
                }
                else
                {
                    gps.Name = name;
                    gps.Coords = pos;
                    gps.GPSColor = color;
                }
            }
        }

        public static void UnsetGPS(ref IMyGps gps)
        {
            if (gpsCachedSet != null)
            {
                if (gps != null)
                {
                    MyAPIGateway.Session.GPS.RemoveLocalGps(gps);
                    gpsCachedSet.Remove(gps);
                    gps = null;
                }
            }
        }

        private void DrawInfo(ref PlanetManager.GravityPlanetData planet)
        {
            bool timeInvalid = false;
            if (double.IsNaN(ttApoapsis) || apoLengthSqr > planet.gravityHeightMaxSqr)
            {
                element_status.Message.Append("Escape Trajectory");
                timeInvalid = true;
            }
            else if (periLengthSqr < planet.gravityHeightMinSqr)
            {
                element_status.Message.Append("Unstable/Decaying Orbit");
                timeInvalid = true;
            }
            else
            {
                element_status.Message.Append("Stable Orbit");
            }

            if (!double.IsNaN(ttPeriapsis) && periLengthSqr > planet.gravityHeightSurfSqr)
            {
                TimeSpan ttP = TimeSpan.FromSeconds((int)ttPeriapsis);
                if (((apoLength / periLength) - 1) > 0.02)
                {
                    element_periapsis.Message.Append($"Periapsis: { ((int)(periLength / 100) / 10F) }km (-{ttP})");
                }
                else
                {
                    element_periapsis.Message.Append($"Periapsis: { ((int)(periLength / 100) / 10F) }km");
                }
            }
            else
            {
                element_periapsis.Message.Append($"Periapsis: N/A");
            }

            if (!double.IsNaN(ttApoapsis) && apoLengthSqr < planet.gravityHeightMaxSqr)
            {
                TimeSpan ttA = TimeSpan.FromSeconds((int)ttApoapsis);
                if (((apoLength / periLength) - 1) > 0.02)
                {
                    element_apoapsis.Message.Append($"Apoapsis: { ((int)(apoLength / 100) / 10F) }km (-{ttA})");
                }
                else
                {
                    element_apoapsis.Message.Append($"Apoapsis: { ((int)(apoLength / 100) / 10F) }km");
                }
            }
            else
            {
                element_apoapsis.Message.Append($"Apoapsis: N/A");
            }

            if (!timeInvalid)
                element_extraInfo.Message.Append($"e: { eLength.ToString("0.000") }   i: { inclination.ToString("0.0") }   T: {TimeSpan.FromSeconds((int)(timeOrbitFactor * 2 * Math.PI))}");
            else
                element_extraInfo.Message.Append($"e: { eLength.ToString("0.000") }   i: { inclination.ToString("0.0") }   T: N/A");
        }
    }
}