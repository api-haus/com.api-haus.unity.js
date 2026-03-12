namespace UnityJS.Entities.Systems.Tick
{
	using Components;
	using Unity.Entities;
	using Unity.Physics.Systems;

	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateAfter(typeof(PhysicsSystemGroup))]
	public partial class JsAfterPhysicsTickSystem : JsTickSystemBase
	{
		protected override JsTickGroup GetTickGroup() => JsTickGroup.AfterPhysics;
	}
}
