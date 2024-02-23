using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace RealisticGravity
{
	public class MathHelpers
	{
		public static float MapRange(float value, float inputMin, float inputMax, float outputMin, float outputMax)
		{
			return outputMin + (value - inputMin) * (outputMax - outputMin) / (inputMax - inputMin);
		}

		public static float MapRangeClamped(float value, float inputMin, float inputMax, float outputMin, float outputMax)
		{
			if (outputMax > outputMin)
				return MathHelper.Clamp(MapRange(value, inputMin, inputMax, outputMin, outputMax), outputMin, outputMax);
			else
				return MathHelper.Clamp(MapRange(value, inputMin, inputMax, outputMin, outputMax), outputMax, outputMin);
		}

		public static Vector3D ProjectOnVector(Vector3D vec, Vector3D otherVec)
		{
			return Vector3D.ProjectOnVector(ref vec, ref otherVec);
		}

		public static Vector3D ProjectOnPlane(Vector3D vec, Vector3D normal)
		{
			return Vector3D.ProjectOnPlane(ref vec, ref normal);
		}

		public static void DrawSphere(MatrixD mat, float radius, Color c)
		{
			MySimpleObjectDraw.DrawTransparentSphere(ref mat, radius, ref c, MySimpleObjectRasterizer.Solid, 20, intensity: 1f, faceMaterial: MyStringId.GetOrCompute("WeaponLaser"), blendType: VRageRender.MyBillboard.BlendTypeEnum.Standard);
		}

		public static void DrawLine(Vector3D start, Vector3D end, float thickness, Color c)
		{
			var v = c.ToVector4();
			MySimpleObjectDraw.DrawLine(start, end, MyStringId.GetOrCompute("WeaponLaser"), ref v, thickness, blendtype: VRageRender.MyBillboard.BlendTypeEnum.Standard);
		}

		public static readonly float PI_2 = (float)(Math.PI * 2);
	}
}
