using Sandbox.Common.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI;
using SpaceEngineers.Game.Entities.Blocks;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace RealisticGravity
{
	public class PlanetManager
	{
		public static PlanetManager Instance { get; private set; }
		public static void Create() { Instance = new PlanetManager(); }
		public static void Destroy() { Instance = null; }

		public Dictionary<long, GravityPlanetData> planetList = new Dictionary<long, GravityPlanetData>();

		public class GravityPlanetData
		{
			public MyPlanet planet;
			public bool isValid;
			public readonly float gravityStrength;
			public readonly double gravityConst;
			public readonly double gravityHeightSurfSqr;
			public readonly double gravityHeightMinSqr;
			public readonly double gravityHeightMaxSqr;
			public readonly float minRenderDistSqr;

			public GravityPlanetData(MyPlanet planet)
			{
				this.planet = planet;
				var builder = planet.GetObjectBuilder() as MyObjectBuilder_Planet;
				gravityStrength = builder.SurfaceGravity;
				gravityHeightSurfSqr = planet.MinimumRadius * planet.MinimumRadius;
				gravityHeightMinSqr = planet.MaximumRadius * planet.MaximumRadius;
				if (Math.Abs(builder.GravityFalloff - 2F) < 0.0001F)
				{
					isValid = true;
					gravityConst = 9.8 * gravityStrength * gravityHeightMinSqr;
					gravityHeightMaxSqr = gravityConst / (9.8 * 0.05);
				}
				else
				{
					isValid = false;
					gravityConst = 9.8 * gravityStrength * Math.Pow(gravityHeightMinSqr, builder.GravityFalloff / 2);
					gravityHeightMaxSqr = Math.Pow(gravityConst / (9.8 * 0.05), 2 / builder.GravityFalloff);
					//MyAPIGateway.Utilities.ShowNotification($"PLANET: {gravityConst} : {gravityHeightMaxSqr}", 50000, MyFontEnum.Green);
				}
				minRenderDistSqr = (float)gravityHeightMinSqr * 0.5F;
			}
		}

		private PlanetManager() { }

		public void Init()
		{
			MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
			MyAPIGateway.Entities.OnEntityRemove += OnEntityRemove;

			if (RealisticGravityCore.ConfigData.OverrideCreatedPlanetGravityFalloff)
			{
				var defs = MyDefinitionManager.Static.GetPlanetsGeneratorsDefinitions();
				foreach (var def in defs)
				{
					def.GravityFalloffPower = 2F;
				}
			}
			
			// Track Planets
			List<IMyVoxelBase> voxelMaps = new List<IMyVoxelBase>();
			MyAPIGateway.Session.VoxelMaps.GetInstances(voxelMaps, (IMyVoxelBase voxelEnt) => { return voxelEnt is MyPlanet; });
			foreach (var planet in voxelMaps)
			{
				if (!string.IsNullOrEmpty(planet.Name))
				{
					//MyLog.Default.WriteLineAndConsole($"PLANET1: {planet.Name}");
					planetList.Add(planet.EntityId, new GravityPlanetData(planet as MyPlanet));
				}
			}
		}

		const double LOW_GRAV_05 = 9.8 * 0.05;
		public GravityPlanetData GetNearestPlanet(Vector3D pos, out int numPlanets)
		{
			GravityPlanetData nearestPlanetData = null;
			double currentHighestGravity = 0;
			numPlanets = 0;

			foreach (var gravData in planetList.Values)
			{
				double distSqr = (gravData.planet.PositionComp.GetPosition() - pos).LengthSquared();
				//MyAPIGateway.Utilities.ShowNotification($"PLANET: {planetList[i].planet.Name} : {planetList[i].gravityHeightMaxSqr}", 5, MyFontEnum.Green);
				if (distSqr < gravData.gravityHeightMinSqr)
				{
					numPlanets = 0;
					return null;
				}

				if (distSqr < gravData.gravityHeightMaxSqr)
					numPlanets += 1;
				else
					continue;

				if (gravData.isValid && (gravData.gravityConst / distSqr) > currentHighestGravity)
				{
					//MyAPIGateway.Utilities.ShowNotification($"PLANET_DIST: {Math.Sqrt(distSqr)}", 5, MyFontEnum.Green);
					nearestPlanetData = gravData;
					currentHighestGravity = gravData.gravityConst / distSqr;
				}
			}

			return nearestPlanetData;
		}

		private void OnEntityAdd(IMyEntity entity)
		{
			if (entity is MyPlanet)
			{
				planetList.Add(entity.EntityId, new GravityPlanetData(entity as MyPlanet));
			}
		}

		private void OnEntityRemove(IMyEntity entity)
		{
			if (entity is MyPlanet)
			{
				if (planetList.ContainsKey(entity.EntityId))
					planetList.Remove(entity.EntityId);
			}
		}
	}
}
