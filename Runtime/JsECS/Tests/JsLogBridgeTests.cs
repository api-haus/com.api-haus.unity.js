namespace UnityJS.Entities.Tests
{
	using NUnit.Framework;

	[TestFixture]
	public unsafe class JsLogBridgeTests : JsBridgeTestFixture
	{
		[Test]
		public void LogDispatch_AllLevels_NoThrow()
		{
			// Calling _log_internal.dispatch directly at each level should not throw
			EvalGlobalVoid("_log_internal.dispatch('debug', 'test debug', '')");
			EvalGlobalVoid("_log_internal.dispatch('info', 'test info', '')");
			EvalGlobalVoid("_log_internal.dispatch('warning', 'test warning', '')");
			EvalGlobalVoid("_log_internal.dispatch('error', 'test error', '')");
			EvalGlobalVoid("_log_internal.dispatch('trace', 'test trace', '')");
		}

		[Test]
		public void LogInfo_FromJS_NoThrow()
		{
			// The JS-side log.info wrapper should work
			EvalGlobalVoid("log.info('hello from test')");
		}
	}
}
