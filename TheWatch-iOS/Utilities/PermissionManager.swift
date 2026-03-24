import Foundation
import CoreLocation
import Contacts
import HealthKit
import UserNotifications
import Observation

// MARK: - Permission Status Enum
enum PermissionStatus: Equatable {
    case notDetermined
    case authorized
    case denied
    case restricted
}

// MARK: - Permission Manager
@Observable
final class PermissionManager: NSObject, CLLocationManagerDelegate {
    // MARK: - Public Properties
    var locationStatus: PermissionStatus = .notDetermined
    var notificationStatus: PermissionStatus = .notDetermined
    var healthKitStatus: PermissionStatus = .notDetermined
    var contactsStatus: PermissionStatus = .notDetermined
    var cameraStatus: PermissionStatus = .notDetermined
    var microphoneStatus: PermissionStatus = .notDetermined

    // Computed property for quick checks
    var allRequiredPermissionsGranted: Bool {
        locationStatus == .authorized &&
        notificationStatus == .authorized &&
        healthKitStatus == .authorized &&
        contactsStatus == .authorized
    }

    // MARK: - Private Properties
    private let locationManager = CLLocationManager()
    private let healthStore = HKHealthStore()

    static let shared = PermissionManager()

    // MARK: - Initialization
    override init() {
        super.init()
        locationManager.delegate = self
        checkAllPermissions()
    }

    // MARK: - Check All Permissions
    /// Refresh all permission statuses — call when app becomes active
    func refreshAllStatuses() {
        checkAllPermissions()
    }

    func checkAllPermissions() {
        checkLocationPermission()
        checkNotificationPermission()
        checkHealthKitPermission()
        checkContactsPermission()
        checkCameraPermission()
        checkMicrophonePermission()
    }

    // MARK: - Location Permission
    func checkLocationPermission() {
        let status = locationManager.authorizationStatus
        locationStatus = mapLocationAuthorizationStatus(status)
        print("[PermissionManager] Location: \(locationStatus)")
    }

    func requestLocationPermission() async -> Bool {
        let status = locationManager.authorizationStatus

        if status == .notDetermined {
            locationManager.requestWhenInUseAuthorization()
            // Wait a bit for the permission dialog to be processed
            try? await Task.sleep(nanoseconds: 500_000_000)
            checkLocationPermission()
        }

        return locationStatus == .authorized
    }

    func requestAlwaysLocationPermission() async -> Bool {
        let status = locationManager.authorizationStatus

        if status == .notDetermined {
            locationManager.requestAlwaysAndWhenInUseAuthorization()
            try? await Task.sleep(nanoseconds: 500_000_000)
        } else if status == .authorizedWhenInUse {
            // Upgrade from When In Use to Always
            locationManager.requestAlwaysAndWhenInUseAuthorization()
            try? await Task.sleep(nanoseconds: 500_000_000)
        }

        checkLocationPermission()
        return locationStatus == .authorized
    }

    // MARK: - Notification Permission
    func checkNotificationPermission() {
        let notificationCenter = UNUserNotificationCenter.current()
        notificationCenter.getNotificationSettings { [weak self] settings in
            DispatchQueue.main.async {
                self?.notificationStatus = self?.mapNotificationAuthorizationStatus(settings.authorizationStatus) ?? .notDetermined
                print("[PermissionManager] Notification: \(self?.notificationStatus ?? .notDetermined)")
            }
        }
    }

    func requestNotificationPermission() async -> Bool {
        let notificationCenter = UNUserNotificationCenter.current()

        do {
            let granted = try await notificationCenter.requestAuthorization(
                options: [.alert, .sound, .badge]
            )

            DispatchQueue.main.async {
                self.notificationStatus = granted ? .authorized : .denied
            }

            return granted
        } catch {
            print("[PermissionManager] Notification request error: \(error)")
            notificationStatus = .denied
            return false
        }
    }

    // MARK: - HealthKit Permission
    func checkHealthKitPermission() {
        // HealthKit doesn't have a simple authorization status check
        // We check by attempting to query a minimal type
        guard HKHealthStore.isHealthDataAvailable() else {
            healthKitStatus = .denied
            return
        }

        let readTypes: Set<HKObjectType> = [
            HKObjectType.quantityType(forIdentifier: .heartRate)!,
            HKObjectType.quantityType(forIdentifier: .stepCount)!,
        ]

        var allAuthorized = true
        for type in readTypes {
            let status = healthStore.authorizationStatus(for: type)
            if status != .sharingAuthorized {
                allAuthorized = false
                break
            }
        }

        healthKitStatus = allAuthorized ? .authorized : .notDetermined
        print("[PermissionManager] HealthKit: \(healthKitStatus)")
    }

    func requestHealthKitPermission() async -> Bool {
        guard HKHealthStore.isHealthDataAvailable() else {
            healthKitStatus = .denied
            return false
        }

        let readTypes: Set<HKObjectType> = [
            HKObjectType.quantityType(forIdentifier: .heartRate)!,
            HKObjectType.quantityType(forIdentifier: .stepCount)!,
        ]

        do {
            try await healthStore.requestAuthorization(toShare: nil, read: readTypes)
            checkHealthKitPermission()
            return healthKitStatus == .authorized
        } catch {
            print("[PermissionManager] HealthKit request error: \(error)")
            healthKitStatus = .denied
            return false
        }
    }

    // MARK: - Contacts Permission
    func checkContactsPermission() {
        let status = CNContactStore.authorizationStatus(for: .contacts)
        contactsStatus = mapContactsAuthorizationStatus(status)
        print("[PermissionManager] Contacts: \(contactsStatus)")
    }

    func requestContactsPermission() async -> Bool {
        let contactStore = CNContactStore()

        do {
            let granted = try await contactStore.requestAccess(for: .contacts)
            DispatchQueue.main.async {
                self.contactsStatus = granted ? .authorized : .denied
            }
            return granted
        } catch {
            print("[PermissionManager] Contacts request error: \(error)")
            contactsStatus = .denied
            return false
        }
    }

    // MARK: - Camera Permission
    func checkCameraPermission() {
        let status = AVCaptureDevice.authorizationStatus(for: .video)
        cameraStatus = mapCameraAuthorizationStatus(status)
        print("[PermissionManager] Camera: \(cameraStatus)")
    }

    func requestCameraPermission() async -> Bool {
        let granted = await AVCaptureDevice.requestAccess(for: .video)
        DispatchQueue.main.async {
            self.cameraStatus = granted ? .authorized : .denied
        }
        return granted
    }

    // MARK: - Microphone Permission
    func checkMicrophonePermission() {
        let status = AVAudioApplication.shared.recordPermission
        microphoneStatus = mapMicrophoneAuthorizationStatus(status)
        print("[PermissionManager] Microphone: \(microphoneStatus)")
    }

    func requestMicrophonePermission() async -> Bool {
        let audioSession = AVAudioSession.sharedInstance()

        do {
            try audioSession.setCategory(.record, mode: .default, options: [])
            let granted = await AVAudioApplication.shared.requestRecordPermission()
            DispatchQueue.main.async {
                self.microphoneStatus = granted ? .authorized : .denied
            }
            return granted
        } catch {
            print("[PermissionManager] Microphone setup error: \(error)")
            microphoneStatus = .denied
            return false
        }
    }

    // MARK: - Settings Navigation
    func openSettings() {
        guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
        UIApplication.shared.open(url)
    }

    // MARK: - Progressive Permission Request Flow
    func requestAllPermissionsProgressively() async {
        // 1. Location first (life-safety critical)
        _ = await requestLocationPermission()

        // 2. Notifications
        _ = await requestNotificationPermission()

        // 3. Health data
        _ = await requestHealthKitPermission()

        // 4. Contacts
        _ = await requestContactsPermission()

        // 5. Camera
        _ = await requestCameraPermission()

        // 6. Microphone
        _ = await requestMicrophonePermission()

        print("[PermissionManager] All permissions requested")
    }

    // MARK: - Permission Status Mapping

    private func mapLocationAuthorizationStatus(_ status: CLAuthorizationStatus) -> PermissionStatus {
        switch status {
        case .notDetermined:
            return .notDetermined
        case .restricted:
            return .restricted
        case .denied:
            return .denied
        case .authorizedAlways, .authorizedWhenInUse:
            return .authorized
        @unknown default:
            return .notDetermined
        }
    }

    private func mapNotificationAuthorizationStatus(_ status: UNAuthorizationStatus) -> PermissionStatus {
        switch status {
        case .notDetermined:
            return .notDetermined
        case .denied:
            return .denied
        case .authorized, .provisional, .ephemeral:
            return .authorized
        @unknown default:
            return .notDetermined
        }
    }

    private func mapContactsAuthorizationStatus(_ status: CNAuthorizationStatus) -> PermissionStatus {
        switch status {
        case .notDetermined:
            return .notDetermined
        case .restricted:
            return .restricted
        case .denied:
            return .denied
        case .authorized:
            return .authorized
        @unknown default:
            return .notDetermined
        }
    }

    private func mapCameraAuthorizationStatus(_ status: AVAuthorizationStatus) -> PermissionStatus {
        switch status {
        case .notDetermined:
            return .notDetermined
        case .restricted:
            return .restricted
        case .denied:
            return .denied
        case .authorized:
            return .authorized
        @unknown default:
            return .notDetermined
        }
    }

    private func mapMicrophoneAuthorizationStatus(_ status: AVAudioSession.RecordPermission) -> PermissionStatus {
        switch status {
        case .undetermined:
            return .notDetermined
        case .denied:
            return .denied
        case .granted:
            return .authorized
        @unknown default:
            return .notDetermined
        }
    }

    // MARK: - CLLocationManagerDelegate

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        DispatchQueue.main.async {
            self.checkLocationPermission()
        }
    }
}

// MARK: - Imports Required for iOS Framework Access
import AVFoundation
