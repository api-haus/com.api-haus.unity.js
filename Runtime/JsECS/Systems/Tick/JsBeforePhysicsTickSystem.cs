namespace UnityJS.Entities.Systems.Tick
{
	using Components;
	using Unity.Entities;
	using Unity.Physics.Systems;

	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateBefore(typeof(PhysicsSystemGroup))]
	public partial class JsBeforePhysicsTickSystem : JsTickSystemBase
	{
		protected override JsTickGroup GetTickGroup() => JsTickGroup.BeforePhysics;
	}
}
