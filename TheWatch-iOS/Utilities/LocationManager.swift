import CoreLocation
import Observation

// MARK: - Tracking Mode Enum
enum LocationTrackingMode {
    /// Standard mode: significant change monitoring, battery efficient
    case normal
    /// Emergency mode: best accuracy, 1-second updates, distance filter 0
    case emergency
}

@Observable
class LocationManager: NSObject, CLLocationManagerDelegate {
    // MARK: - Public Properties
    var userLocation: CLLocationCoordinate2D?
    var isAuthorized: Bool = false
    var authorizationStatus: CLAuthorizationStatus = .notDetermined
    var trackingMode: LocationTrackingMode = .normal
    var lastLocationUpdate: Date?
    var locationUpdateCount: Int = 0

    // MARK: - Private Properties
    private let locationManager = CLLocationManager()
    private var backgroundTaskID: UIBackgroundTaskIdentifier = .invalid

    static let shared = LocationManager()

    // MARK: - Initialization
    override init() {
        super.init()
        locationManager.delegate = self
        configureLocationManager()
        authorizationStatus = locationManager.authorizationStatus
        monitorAuthorizationChanges()
    }

    // MARK: - Configuration
    private func configureLocationManager() {
        // Life-safety critical: Allow background location updates
        locationManager.allowsBackgroundLocationUpdates = true

        // Do NOT pause updates automatically - life-safety requirement
        locationManager.pausesLocationUpdatesAutomatically = false

        // Show background location indicator (blue bar)
        if #available(iOS 14.0, *) {
            locationManager.showsBackgroundLocationIndicator = true
        }

        // Activity type for navigation (most appropriate for safety app)
        locationManager.activityType = .otherNavigation

        // Configure for normal mode initially
        switchToNormalMode()
    }

    // MARK: - Authorization Management
    func requestWhenInUseAuthorization() {
        locationManager.requestWhenInUseAuthorization()
    }

    func requestAlwaysAuthorization() {
        locationManager.requestAlwaysAndWhenInUseAuthorization()
    }

    // MARK: - Tracking Mode Control
    func switchToEmergencyMode() {
        print("[LocationManager] Switching to EMERGENCY tracking mode")
        trackingMode = .emergency

        // Best possible accuracy
        locationManager.desiredAccuracy = kCLLocationAccuracyBest

        // Minimum distance filter (0 = update on any movement)
        locationManager.distanceFilter = 0

        // Stop significant location monitoring if running
        locationManager.stopMonitoringSignificantLocationChanges()

        // Start continuous high-frequency updates
        locationManager.startUpdatingLocation()

        // Register background task to ensure continuous updates
        registerBackgroundTask()
    }

    func switchToNormalMode() {
        print("[LocationManager] Switching to NORMAL tracking mode")
        trackingMode = .normal

        // Good accuracy (within 100m)
        locationManager.desiredAccuracy = kCLLocationAccuracyNearestTenMeters

        // Stop continuous updates
        locationManager.stopUpdatingLocation()

        // Use significant location change monitoring for battery efficiency
        startSignificantLocationMonitoring()

        // End background task
        endBackgroundTask()
    }

    // MARK: - Significant Location Monitoring (Battery Efficient)
    private func startSignificantLocationMonitoring() {
        locationManager.stopUpdatingLocation()
        locationManager.startMonitoringSignificantLocationChanges()
        print("[LocationManager] Started significant location change monitoring")
    }

    // MARK: - Background Task Management
    private func registerBackgroundTask() {
        guard backgroundTaskID == .invalid else { return }

        backgroundTaskID = UIApplication.shared.beginBackgroundTask { [weak self] in
            self?.endBackgroundTask()
        }

        print("[LocationManager] Background task registered: \(backgroundTaskID.rawValue)")
    }

    private func endBackgroundTask() {
        guard backgroundTaskID != .invalid else { return }

        UIApplication.shared.endBackgroundTask(backgroundTaskID)
        backgroundTaskID = .invalid
        print("[LocationManager] Background task ended")
    }

    // MARK: - Location Authorization Monitoring
    private func monitorAuthorizationChanges() {
        // This will be called automatically on authorization changes
        DispatchQueue.main.async {
            self.updateAuthorizationStatus()
        }
    }

    private func updateAuthorizationStatus() {
        let status = locationManager.authorizationStatus
        self.authorizationStatus = status

        // Only AlwaysAndWhenInUse provides background tracking
        let isBackgroundAuthorized = status == .authorizedAlways
        self.isAuthorized = status == .authorizedAlways || status == .authorizedWhenInUse

        print("[LocationManager] Authorization status: \(status.description)")

        if isBackgroundAuthorized {
            print("[LocationManager] Background location updates enabled")
        }
    }

    // MARK: - CLLocationManagerDelegate

    func locationManagerDidChangeAuthorization(_ manager: CLLocationManager) {
        DispatchQueue.main.async {
            self.updateAuthorizationStatus()

            // Restart location updates if we just gained permission
            if self.isAuthorized {
                if self.trackingMode == .emergency {
                    self.switchToEmergencyMode()
                } else {
                    self.switchToNormalMode()
                }
            } else {
                manager.stopUpdatingLocation()
                manager.stopMonitoringSignificantLocationChanges()
            }
        }
    }

    func locationManager(
        _ manager: CLLocationManager,
        didUpdateLocations locations: [CLLocation]
    ) {
        guard let location = locations.last else { return }

        DispatchQueue.main.async {
            self.userLocation = location.coordinate
            self.lastLocationUpdate = Date()
            self.locationUpdateCount += 1

            let mode = self.trackingMode == .emergency ? "EMERGENCY" : "NORMAL"
            print("[LocationManager] [\(mode)] Update #\(self.locationUpdateCount): \(location.coordinate.latitude), \(location.coordinate.longitude) (accuracy: \(location.horizontalAccuracy)m)")
        }
    }

    func locationManager(
        _ manager: CLLocationManager,
        didFailWithError error: Error
    ) {
        let clError = error as? CLError
        print("[LocationManager] Error: \(error.localizedDescription) (code: \(clError?.code.rawValue ?? -1))")

        // Don't restart on temporary errors
        if let clError = clError {
            switch clError.code {
            case .locationUnknown:
                print("[LocationManager] Location temporarily unavailable")
            case .denied:
                print("[LocationManager] Location access denied")
            case .network:
                print("[LocationManager] Network error - location unavailable")
            default:
                print("[LocationManager] Other error: \(clError.code)")
            }
        }
    }
}

// MARK: - Helper Extensions
extension CLAuthorizationStatus {
    var description: String {
        switch self {
        case .notDetermined:
            return "Not Determined"
        case .restricted:
            return "Restricted"
        case .denied:
            return "Denied"
        case .authorizedAlways:
            return "Authorized Always"
        case .authorizedWhenInUse:
            return "Authorized When In Use"
        @unknown default:
            return "Unknown"
        }
    }
}
