namespace UnityJS.Entities.Systems.Tick
{
	using Components;
	using Unity.Entities;
	using Unity.Transforms;

	[UpdateInGroup(typeof(SimulationSystemGroup))]
	[UpdateAfter(typeof(TransformSystemGroup))]
	public partial class JsAfterTransformTickSystem : JsTickSystemBase
	{
		protected override JsTickGroup GetTickGroup() => JsTickGroup.AfterTransform;
	}
}
