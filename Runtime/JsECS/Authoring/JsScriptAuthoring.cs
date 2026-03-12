namespace UnityJS.Entities.Authoring
{
	using Components;
	using UnityEngine;

	[RequireComponent(typeof(JsScriptBufferAuthoring))]
	public class JsScriptAuthoring : MonoBehaviour
	{
		[Tooltip("File name without .js extension, relative to Assets/StreamingAssets/js")]
		public JsScriptAssetReference script;

		void Reset()
		{
			EnsureBufferAuthoring();
		}

		void OnValidate()
		{
			EnsureBufferAuthoring();
		}

		void EnsureBufferAuthoring()
		{
			if (TryGetComponent<JsScriptBufferAuthoring>(out _))
				return;

			gameObject.AddComponent<JsScriptBufferAuthoring>();
		}
	}
}
