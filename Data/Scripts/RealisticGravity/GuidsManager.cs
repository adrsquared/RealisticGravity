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
		public readonly static Guid GAS_GIANT_GRAVITY_DATA = new Guid("d34ff10e-461d-4511-be49-043bae9b7701");

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
