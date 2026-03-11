namespace UnityJS.Entities.Components
{
	using Unity.Collections;
	using Unity.Entities;

	public struct JsEvent : IBufferElementData
	{
		public FixedString32Bytes eventName;
		public Entity source;
		public Entity target;
		public int intParam;
		public float floatParam;
	}
}
