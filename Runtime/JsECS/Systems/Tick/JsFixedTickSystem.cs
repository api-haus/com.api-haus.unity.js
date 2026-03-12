namespace UnityJS.Entities.Systems.Tick
{
	using Components;
	using Unity.Entities;

	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	public partial class JsFixedTickSystem : JsTickSystemBase
	{
		protected override JsTickGroup GetTickGroup() => JsTickGroup.Fixed;
	}
}
