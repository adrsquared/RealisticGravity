using Sandbox.Game.EntityComponents;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.ModAPI;

namespace RealisticGravity
{
	class GuidsManager
	{
		public readonly static Guid STAR_GRAVITY_DATA = new Guid("f6649d92-33f9-4cb4-b38b-69bf19dd3001");

		public static void SetStorageValue(IMyEntity entity, Guid guid, string value)
		{
			if (entity == null)
				return;

			if (entity.Storage == null)
				entity.Storage = new MyModStorageComponent();

			entity.Storage.SetValue(guid, value);
		}

		public static void RemoveStorageValue(IMyEntity entity, Guid guid)
		{
			if (entity != null && entity.Storage != null && entity.Storage.ContainsKey(guid))
			{
				entity.Storage.RemoveValue(guid);
			}
		}

		public static string GetStorageValue(IMyEntity entity, Guid guid)
		{
			if (entity != null && entity.Storage != null && entity.Storage.ContainsKey(guid))
			{
				return entity.Storage.GetValue(guid);
			}

			return null;
		}
	}
}
