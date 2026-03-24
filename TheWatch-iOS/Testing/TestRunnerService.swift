import Foundation
import SwiftUI

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: TestRunnerService receives test step commands from the MAUI dashboard
// via SignalR, executes them via TestStepExecutorRegistry, and reports results.
//
// Flow:
// 1. Dashboard calls StartRunAsync → hub sends "ExecuteTestStep" to device group
// 2. HubMessageRouterProtocol delivers (runId, step) to this service
// 3. Service looks up executor for step.action from TestStepExecutorRegistry
// 4. Executor runs against the live app via TestExecutionContext
// 5. Result is recorded and sent back via HubEventSenderProtocol
// 6. Dashboard dispatches the next step (or completes the run)
//
// Uses Swift concurrency throughout — all execution is async/await.
// Cancellation is supported via Task.
//
// When Agent 4's SignalR client merges:
//   Replace MockHubMessageRouter with a real router listening on the hub connection.
//   Replace MockHubEventSender with a real sender calling hub.send().
//
// Example:
// ```swift
// let runner = TestRunnerService.shared
// runner.initialize(
//     router: MockHubMessageRouter(),
//     sender: MockHubEventSender(),
//     context: MockTestExecutionContext.create()
// )
// // Observe state
// Text(runner.runState?.status.rawValue ?? "Idle")
// ```

/// Port protocol for receiving test commands from the SignalR hub.
protocol HubMessageRouterProtocol: Sendable {
    /// Register a handler for incoming test step execution commands.
    func onTestStepReceived(_ handler: @escaping @Sendable (String, TestStep) async -> Void)

    /// Register a handler for test run cancellation commands.
    func onTestRunCancelled(_ handler: @escaping @Sendable (String) async -> Void)
}

/// Port protocol for sending test results back to the SignalR hub.
protocol HubEventSenderProtocol: Sendable {
    /// Report the result of a completed test step back to the dashboard.
    func reportStepResult(
        runId: String,
        stepId: String,
        passed: Bool,
        screenshot: String?,
        errorMessage: String?
    ) async

    /// Report the overall run status to the dashboard.
    func reportRunStatus(_ runState: TestRunState) async
}

/// Core service that receives test steps from the MAUI dashboard via SignalR,
/// executes them against the live application, and reports results back.
@Observable
final class TestRunnerService {
    static let shared = TestRunnerService()

    private let executorRegistry = TestStepExecutorRegistry.shared
    private let logger = WatchLogger.shared

    /// Observable state of the current test run. Nil when idle.
    var runState: TestRunState?

    private var hubEventSender: HubEventSenderProtocol?
    private var executionContext: TestExecutionContext?
    private var executionTask: Task<Void, Never>?

    private init() {}

    /// Initialize the service with hub communication interfaces and execution context.
    /// Called during app startup after navigation is ready.
    func initialize(
        router: HubMessageRouterProtocol,
        sender: HubEventSenderProtocol,
        context: TestExecutionContext
    ) {
        self.hubEventSender = sender
        self.executionContext = context

        router.onTestStepReceived { [weak self] runId, step in
            await self?.onStepReceived(runId: runId, step: step)
        }

        router.onTestRunCancelled { [weak self] runId in
            await self?.onRunCancelled(runId: runId)
        }

        let actions = executorRegistry.supportedActions.map(\.rawValue).sorted().joined(separator: ", ")
        logger.information(
            source: "TestRunnerService",
            template: "Test runner initialized. Supported actions: {Actions}",
            properties: ["Actions": actions]
        )
    }

    /// Handle an incoming test step from the dashboard.
    @MainActor
    func onStepReceived(runId: String, step: TestStep) async {
        guard let ctx = executionContext else {
            logger.error(
                source: "TestRunnerService",
                template: "Received step {StepId} but execution context is not initialized",
                properties: ["StepId": step.id]
            )
            return
        }

        // Initialize or update run state
        if runState == nil || runState?.runId != runId {
            runState = TestRunState(runId: runId, currentStep: step)
        } else {
            runState?.currentStep = step
        }

        logger.information(
            source: "TestRunnerService",
            template: "Executing step {StepId} ({Order}): {Action} on {Screen} target='{Target}'",
            properties: [
                "StepId": step.id,
                "Order": "\(step.order)",
                "Action": step.action,
                "Screen": step.screenName,
                "Target": step.target,
                "RunId": runId
            ]
        )

        // Cancel any in-flight execution
        executionTask?.cancel()

        executionTask = Task { [weak self] in
            guard let self else { return }
            let result = await self.executeStep(step, context: ctx)
            await self.recordResult(runId: runId, result: result)
        }
    }

    /// Execute a single step using the appropriate executor.
    private func executeStep(_ step: TestStep, context: TestExecutionContext) async -> TestStepResult {
        guard let executor = executorRegistry.executor(for: step.action) else {
            let supported = executorRegistry.supportedActions.map(\.rawValue).sorted().joined(separator: ", ")
            logger.error(
                source: "TestRunnerService",
                template: "No executor registered for action '{Action}'. Skipping step {StepId}.",
                properties: ["Action": step.action, "StepId": step.id]
            )
            return step.toResult(
                passed: false,
                durationMs: 0,
                errorMessage: "Unsupported action: '\(step.action)'. Supported: \(supported)"
            )
        }

        do {
            return await executor.execute(step: step, context: context)
        } catch {
            logger.error(
                source: "TestRunnerService",
                template: "Step {StepId} threw exception: {Error}",
                properties: ["StepId": step.id, "Error": error.localizedDescription]
            )
            return step.toResult(
                passed: false,
                durationMs: 0,
                errorMessage: "FATAL: \(type(of: error)): \(error.localizedDescription)"
            )
        }
    }

    /// Record a step result locally and report it back to the dashboard.
    @MainActor
    private func recordResult(runId: String, result: TestStepResult) async {
        guard let state = runState else { return }

        state.results.append(result)
        state.completedSteps = state.results.count
        state.passedSteps = state.results.filter(\.passed).count
        state.failedSteps = state.results.filter { !$0.passed }.count
        state.currentStep = nil

        logger.information(
            source: "TestRunnerService",
            template: "Step {StepId} {Result} in {DurationMs}ms — run {RunId} [{Completed} done]",
            properties: [
                "StepId": result.stepId,
                "Result": result.passed ? "PASS" : "FAIL",
                "DurationMs": "\(result.durationMs)",
                "RunId": runId,
                "Completed": "\(state.results.count)"
            ]
        )

        // Report back to dashboard
        await hubEventSender?.reportStepResult(
            runId: runId,
            stepId: result.stepId,
            passed: result.passed,
            screenshot: result.screenshot,
            errorMessage: result.errorMessage
        )
    }

    /// Handle run cancellation from the dashboard.
    @MainActor
    func onRunCancelled(runId: String) async {
        guard let state = runState, state.runId == runId else { return }

        executionTask?.cancel()
        state.status = .cancelled

        logger.warning(
            source: "TestRunnerService",
            template: "Test run {RunId} cancelled by dashboard",
            properties: ["RunId": runId]
        )
    }

    /// Tear down the service.
    func shutdown() {
        executionTask?.cancel()
        runState = nil
        logger.information(
            source: "TestRunnerService",
            template: "Test runner shut down"
        )
    }
}
