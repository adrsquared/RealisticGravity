using ProtoBuf;
using VRageMath;

namespace RealisticGravity
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class OrbitSettingsConfig
    {
        [ProtoMember(1)]
        public float GlobalMaxSpeedMultiplier_LargeGrid = -1F;
        [ProtoMember(2)]
        public float GlobalMaxSpeedMultiplier_SmallGrid = -1F;
        [ProtoMember(3)]
        public float CharacterGravityMultiplier = 2F;
        [ProtoMember(4)]
        public bool OverridePlanetGravityFalloff = true;
        [ProtoMember(5)]
        public bool EnforceSingleGravityWell = true;
        [ProtoMember(6)]
        public bool ShowCharacterOrbitInfo = false;
        [ProtoMember(7)]
        public bool ShowCharacterOrbitPath = false;
        [ProtoMember(8)]
        public bool ShowGridOrbitInfo = true;
        [ProtoMember(9)]
        public bool ShowGridOrbitPath = true;
        [ProtoMember(10)]
        public bool ShowGridOrbitPathEnemy = false;
        [ProtoMember(11)]
        public double GridOrbitPathMaxDrawDistance = 500000;
        [ProtoMember(12)]
        public bool ShowGridOrbitGps = true;
        [ProtoMember(13)]
        public bool ShowPeriApoGps = true;
        [ProtoMember(14)]
        public float OrbitPathLineThickness = 1F;
        [ProtoMember(15)]
        public int OrbitRenderDivisions = 60;
        [ProtoMember(16)]
        public Vector2 OrbitInfoScreenOffset = new Vector2(-0.7F, -0.55F);
        [ProtoMember(17)]
        public Vector3 ColorMyOrbitPath = new Vector3(1F, 1F, 0F);
        [ProtoMember(18)]
        public Vector3 ColorGridOrbitPathFriendly = new Vector3(0.8F, 1F, 0.8F);
        [ProtoMember(19)]
        public Vector3 ColorGridOrbitPathNeutral = new Vector3(1F, 1F, 1F);
        [ProtoMember(20)]
        public Vector3 ColorGridOrbitPathEnemy = new Vector3(1F, 0.7F, 0.7F);
        [ProtoMember(21)]
        public Vector3 ColorPeriApoGps = new Vector3(0.8F, 0.8F, 1F);
    }
}
