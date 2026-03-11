namespace UnityJS.Entities.Components
{
	using System;
	using Unity.Entities;

	[Serializable]
	public struct JsPlayerTag : IComponentData
	{
		public int playerId;
	}
}
