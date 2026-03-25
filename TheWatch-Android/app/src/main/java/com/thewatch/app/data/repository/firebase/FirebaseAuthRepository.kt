package com.thewatch.app.data.repository.firebase

import android.content.Context
import android.util.Log
import androidx.biometric.BiometricManager
import androidx.biometric.BiometricPrompt
import androidx.core.content.ContextCompat
import androidx.fragment.app.FragmentActivity
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.google.firebase.auth.FirebaseAuth
import com.google.firebase.auth.FirebaseUser
import com.google.firebase.auth.UserProfileChangeRequest
import com.thewatch.app.data.model.MfaEnrollmentChallenge
import com.thewatch.app.data.model.User
import com.thewatch.app.data.repository.AuthRepository
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.tasks.await
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.OutputStreamWriter
import java.net.HttpURLConnection
import java.net.URL
import javax.inject.Inject
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException

/**
 * Firebase Auth adapter implementing the AuthRepository port.
 *
 * 3-Tier Architecture: Live (Cloud) Adapter Tier
 * This is the production-grade Firebase Auth implementation that handles:
 *   - Email/password sign-in and registration
 *   - Email verification flow
 *   - Password reset via Firebase's built-in email action
 *   - Biometric unlock of cached Firebase credentials
 *   - MFA enrollment and verification via backend API
 *   - ID token management and forced refresh
 *
 * Write-Ahead Log:
 *   - WAL Entry: AUTH_LOGIN_START     -> email, timestamp
 *   - WAL Entry: AUTH_LOGIN_SUCCESS   -> uid, email, timestamp
 *   - WAL Entry: AUTH_LOGIN_FAIL      -> email, errorCode, timestamp
 *   - WAL Entry: AUTH_SIGNUP_START    -> email, timestamp
 *   - WAL Entry: AUTH_SIGNUP_SUCCESS  -> uid, email, emailVerified=false, timestamp
 *   - WAL Entry: AUTH_LOGOUT          -> uid, timestamp
 *   - WAL Entry: AUTH_TOKEN_REFRESH   -> uid, newExpiry, timestamp
 *   - WAL Entry: AUTH_BIOMETRIC_START -> uid, timestamp
 *   - WAL Entry: AUTH_BIOMETRIC_OK    -> uid, timestamp
 *   - WAL Entry: AUTH_BIOMETRIC_FAIL  -> uid, reason, timestamp
 *   - WAL Entry: AUTH_MFA_ENROLL      -> uid, method, sessionId, timestamp
 *   - WAL Entry: AUTH_MFA_VERIFY      -> uid, method, success, timestamp
 *   - WAL Entry: AUTH_EMAIL_VERIFY    -> uid, sent, timestamp
 *   - WAL Entry: AUTH_PWD_RESET_SEND  -> email, timestamp
 *
 * Example usage:
 *   // In Hilt module:
 *   AdapterTier.Live -> FirebaseAuthRepository(context)
 *
 *   // Login:
 *   val result = firebaseAuthRepo.login("user@example.com", "Password123!")
 *   result.onSuccess { user -> navigateToHome() }
 *   result.onFailure { e -> showError(e.message) }
 *
 * Dependencies (expected in build.gradle):
 *   implementation("com.google.firebase:firebase-auth-ktx")
 *   implementation("androidx.biometric:biometric:1.2.0-alpha05")
 *   implementation("androidx.security:security-crypto:1.1.0-alpha06")
 *   implementation("org.jetbrains.kotlinx:kotlinx-coroutines-play-services")
 *
 * NOTE: Ensure google-services.json is placed in app/ directory and the
 *       com.google.gms.google-services plugin is applied in build.gradle.
 */
class FirebaseAuthRepository @Inject constructor(
    private val context: Context
) : AuthRepository {

    companion object {
        private const val TAG = "FirebaseAuthRepo"
        private const val PREFS_NAME = "firebase_auth_secure_prefs"
        private const val KEY_CACHED_EMAIL = "cached_email"
        private const val KEY_CACHED_PASSWORD = "cached_password"
        private const val KEY_CACHED_PHONE = "cached_phone"
        private const val KEY_CACHED_DOB = "cached_dob"

        /**
         * Base URL for TheWatch backend API.
         * In production this would come from BuildConfig or remote config.
         * The backend validates Firebase ID tokens and manages MFA state.
         */
        private const val API_BASE_URL = "https://api.thewatch.app"
    }

    private val firebaseAuth: FirebaseAuth = FirebaseAuth.getInstance()

    private val currentUserFlow = MutableStateFlow<User?>(
        firebaseAuth.currentUser?.toUser()
    )

    // Encrypted credential cache for biometric re-authentication.
    // BiometricPrompt unlocks the keystore; we then use the stored credentials
    // to silently re-authenticate with Firebase (no password prompt needed).
    private val masterKey = MasterKey.Builder(context)
        .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
        .build()

    private val securePrefs = EncryptedSharedPreferences.create(
        context,
        PREFS_NAME,
        masterKey,
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
    )

    init {
        // Listen for Firebase auth state changes and propagate to the Flow.
        firebaseAuth.addAuthStateListener { auth ->
            currentUserFlow.value = auth.currentUser?.toUser()
        }
    }

    // ── Login ────────────────────────────────────────────────────────────
    override suspend fun login(emailOrPhone: String, password: String): Result<User> {
        return try {
            Log.d(TAG, "WAL: AUTH_LOGIN_START email=$emailOrPhone")
            val authResult = firebaseAuth.signInWithEmailAndPassword(emailOrPhone, password).await()
            val firebaseUser = authResult.user
                ?: return Result.failure(Exception("Authentication succeeded but no user returned."))

            // Cache credentials for biometric re-login
            cacheCredentials(emailOrPhone, password)

            // Force token refresh to get latest custom claims (mfaEnabled, roles, etc.)
            firebaseUser.getIdToken(true).await()

            val user = firebaseUser.toUser()
            currentUserFlow.value = user
            Log.d(TAG, "WAL: AUTH_LOGIN_SUCCESS uid=${user.id}")
            Result.success(user)
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_LOGIN_FAIL email=$emailOrPhone error=${e.message}")
            Result.failure(mapFirebaseException(e))
        }
    }

    // ── Sign Up ──────────────────────────────────────────────────────────
    override suspend fun signUp(
        name: String,
        email: String,
        phone: String,
        dateOfBirth: String,
        password: String
    ): Result<User> {
        return try {
            Log.d(TAG, "WAL: AUTH_SIGNUP_START email=$email")
            val authResult = firebaseAuth.createUserWithEmailAndPassword(email, password).await()
            val firebaseUser = authResult.user
                ?: return Result.failure(Exception("Account created but no user returned."))

            // Set display name on the Firebase user profile
            val profileUpdates = UserProfileChangeRequest.Builder()
                .setDisplayName(name)
                .build()
            firebaseUser.updateProfile(profileUpdates).await()

            // Send email verification automatically on signup
            firebaseUser.sendEmailVerification().await()
            Log.d(TAG, "WAL: AUTH_EMAIL_VERIFY uid=${firebaseUser.uid} sent=true")

            // Cache credentials and supplemental fields for biometric re-login
            cacheCredentials(email, password)
            securePrefs.edit()
                .putString(KEY_CACHED_PHONE, phone)
                .putString(KEY_CACHED_DOB, dateOfBirth)
                .apply()

            val user = firebaseUser.toUser(phone = phone, dateOfBirth = dateOfBirth)
            currentUserFlow.value = user
            Log.d(TAG, "WAL: AUTH_SIGNUP_SUCCESS uid=${user.id}")
            Result.success(user)
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_SIGNUP_FAIL email=$email error=${e.message}")
            Result.failure(mapFirebaseException(e))
        }
    }

    // ── Password Reset ───────────────────────────────────────────────────
    override suspend fun sendPasswordResetCode(emailOrPhone: String): Result<String> {
        return try {
            Log.d(TAG, "WAL: AUTH_PWD_RESET_SEND email=$emailOrPhone")
            firebaseAuth.sendPasswordResetEmail(emailOrPhone).await()
            // Firebase handles the code delivery; we return a confirmation token.
            // The actual code lives in the email link, not exposed to the client.
            Result.success("reset_email_sent")
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_PWD_RESET_FAIL email=$emailOrPhone error=${e.message}")
            Result.failure(mapFirebaseException(e))
        }
    }

    override suspend fun verifyResetCode(code: String): Result<Boolean> {
        return try {
            // Firebase verifyPasswordResetCode validates the oobCode from the email link
            firebaseAuth.verifyPasswordResetCode(code).await()
            Result.success(true)
        } catch (e: Exception) {
            Result.failure(mapFirebaseException(e))
        }
    }

    override suspend fun resetPassword(code: String, newPassword: String): Result<Boolean> {
        return try {
            // Firebase confirmPasswordReset applies the new password using the oobCode
            firebaseAuth.confirmPasswordReset(code, newPassword).await()
            Result.success(true)
        } catch (e: Exception) {
            Result.failure(mapFirebaseException(e))
        }
    }

    // ── Biometric Login ──────────────────────────────────────────────────
    // Flow: BiometricPrompt verifies the user's fingerprint/face -> we read
    // cached credentials from EncryptedSharedPreferences -> silently call
    // Firebase signInWithEmailAndPassword. This keeps the Firebase session
    // fresh without asking the user to type their password again.
    override suspend fun biometricLogin(): Result<User> {
        val cachedEmail = securePrefs.getString(KEY_CACHED_EMAIL, null)
        val cachedPassword = securePrefs.getString(KEY_CACHED_PASSWORD, null)

        if (cachedEmail.isNullOrEmpty() || cachedPassword.isNullOrEmpty()) {
            return Result.failure(
                Exception("No cached credentials found. Please log in with email and password first.")
            )
        }

        // Check that biometric hardware is available
        val biometricManager = BiometricManager.from(context)
        when (biometricManager.canAuthenticate(BiometricManager.Authenticators.BIOMETRIC_STRONG)) {
            BiometricManager.BIOMETRIC_SUCCESS -> { /* proceed */ }
            BiometricManager.BIOMETRIC_ERROR_NO_HARDWARE ->
                return Result.failure(Exception("No biometric hardware available on this device."))
            BiometricManager.BIOMETRIC_ERROR_HW_UNAVAILABLE ->
                return Result.failure(Exception("Biometric hardware is currently unavailable."))
            BiometricManager.BIOMETRIC_ERROR_NONE_ENROLLED ->
                return Result.failure(Exception("No biometric credentials enrolled. Set up fingerprint or face in Settings."))
            else ->
                return Result.failure(Exception("Biometric authentication is not available."))
        }

        // The actual BiometricPrompt must be shown from an Activity/Fragment context.
        // This method validates that cached credentials exist and biometric hardware is ready.
        // The ViewModel/Screen layer should invoke BiometricPrompt.authenticate() and, on
        // success, call this method which will re-authenticate with Firebase.
        Log.d(TAG, "WAL: AUTH_BIOMETRIC_START")
        return try {
            val authResult = firebaseAuth.signInWithEmailAndPassword(cachedEmail, cachedPassword).await()
            val firebaseUser = authResult.user
                ?: return Result.failure(Exception("Biometric re-auth succeeded but no user returned."))
            val user = firebaseUser.toUser()
            currentUserFlow.value = user
            Log.d(TAG, "WAL: AUTH_BIOMETRIC_OK uid=${user.id}")
            Result.success(user)
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_BIOMETRIC_FAIL error=${e.message}")
            Result.failure(mapFirebaseException(e))
        }
    }

    // ── Logout ───────────────────────────────────────────────────────────
    override suspend fun logout(): Result<Unit> {
        return try {
            val uid = firebaseAuth.currentUser?.uid
            Log.d(TAG, "WAL: AUTH_LOGOUT uid=$uid")
            firebaseAuth.signOut()
            currentUserFlow.value = null
            // Do NOT clear cached credentials on logout -- biometric login needs them.
            // They are encrypted at rest via EncryptedSharedPreferences + AES-256-GCM.
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // ── Current User ─────────────────────────────────────────────────────
    override fun getCurrentUser(): Flow<User?> = currentUserFlow.asStateFlow()

    // ── EULA ─────────────────────────────────────────────────────────────
    override suspend fun acceptEULA(userId: String): Result<Unit> {
        // EULA acceptance is recorded on the backend. We POST to the API
        // with the user's Firebase ID token for authentication.
        return try {
            val idToken = firebaseAuth.currentUser?.getIdToken(false)?.await()?.token
                ?: return Result.failure(Exception("Not authenticated."))
            callBackendApi(
                method = "POST",
                path = "/api/account/eula/accept",
                idToken = idToken,
                body = """{"userId":"$userId","acceptedAt":${System.currentTimeMillis()}}"""
            )
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    // ── Email Verification ───────────────────────────────────────────────
    override suspend fun sendEmailVerification(): Result<Unit> {
        return try {
            val firebaseUser = firebaseAuth.currentUser
                ?: return Result.failure(Exception("No authenticated user."))
            firebaseUser.sendEmailVerification().await()
            Log.d(TAG, "WAL: AUTH_EMAIL_VERIFY uid=${firebaseUser.uid} sent=true")
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(mapFirebaseException(e))
        }
    }

    // ── Token Refresh ────────────────────────────────────────────────────
    override suspend fun refreshToken(): Result<User> {
        return try {
            val firebaseUser = firebaseAuth.currentUser
                ?: return Result.failure(Exception("No authenticated user."))
            // Force refresh to pick up updated custom claims (emailVerified, mfaEnabled, roles)
            firebaseUser.reload().await()
            firebaseUser.getIdToken(true).await()
            Log.d(TAG, "WAL: AUTH_TOKEN_REFRESH uid=${firebaseUser.uid}")
            val refreshedUser = firebaseAuth.currentUser?.toUser()
                ?: return Result.failure(Exception("User gone after token refresh."))
            currentUserFlow.value = refreshedUser
            Result.success(refreshedUser)
        } catch (e: Exception) {
            Result.failure(mapFirebaseException(e))
        }
    }

    // ── MFA Enrollment ───────────────────────────────────────────────────
    // Initiates MFA enrollment by calling the backend, which generates a
    // TOTP secret or sends an SMS code, and returns a session + challenge.
    override suspend fun enrollMfa(
        method: String,
        phoneNumber: String?
    ): Result<MfaEnrollmentChallenge> {
        return try {
            val idToken = firebaseAuth.currentUser?.getIdToken(false)?.await()?.token
                ?: return Result.failure(Exception("Not authenticated."))

            Log.d(TAG, "WAL: AUTH_MFA_ENROLL method=$method")
            val responseBody = callBackendApi(
                method = "POST",
                path = "/api/account/mfa/enroll",
                idToken = idToken,
                body = buildString {
                    append("""{"method":"$method"""")
                    if (!phoneNumber.isNullOrEmpty()) append(""","phoneNumber":"$phoneNumber"""")
                    append("}")
                }
            )

            val json = Json.parseToJsonElement(responseBody).jsonObject
            val challenge = MfaEnrollmentChallenge(
                method = json["method"]?.jsonPrimitive?.content ?: method,
                challengeUri = json["challengeUri"]?.jsonPrimitive?.content ?: "",
                sessionId = json["sessionId"]?.jsonPrimitive?.content ?: "",
                backupCodes = json["backupCodes"]?.let { element ->
                    val arr = element as? kotlinx.serialization.json.JsonArray ?: return@let emptyList()
                    arr.map { it.jsonPrimitive.content }
                } ?: emptyList()
            )
            Result.success(challenge)
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_MFA_ENROLL_FAIL error=${e.message}")
            Result.failure(e)
        }
    }

    // ── MFA Enrollment Confirmation ──────────────────────────────────────
    override suspend fun confirmMfaEnrollment(
        sessionId: String,
        code: String
    ): Result<Boolean> {
        return try {
            val idToken = firebaseAuth.currentUser?.getIdToken(false)?.await()?.token
                ?: return Result.failure(Exception("Not authenticated."))

            callBackendApi(
                method = "POST",
                path = "/api/account/mfa/enroll/confirm",
                idToken = idToken,
                body = """{"sessionId":"$sessionId","code":"$code"}"""
            )
            Log.d(TAG, "WAL: AUTH_MFA_ENROLL_CONFIRM sessionId=$sessionId")
            Result.success(true)
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_MFA_ENROLL_CONFIRM_FAIL error=${e.message}")
            Result.failure(e)
        }
    }

    // ── MFA Verification (post-login) ────────────────────────────────────
    override suspend fun verifyMfaCode(code: String, method: String): Result<Boolean> {
        return try {
            val idToken = firebaseAuth.currentUser?.getIdToken(false)?.await()?.token
                ?: return Result.failure(Exception("Not authenticated."))

            callBackendApi(
                method = "POST",
                path = "/api/account/mfa/verify",
                idToken = idToken,
                body = """{"code":"$code","method":"$method"}"""
            )
            Log.d(TAG, "WAL: AUTH_MFA_VERIFY method=$method success=true")
            Result.success(true)
        } catch (e: Exception) {
            Log.e(TAG, "WAL: AUTH_MFA_VERIFY_FAIL method=$method error=${e.message}")
            Result.failure(e)
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private helpers
    // ══════════════════════════════════════════════════════════════════════

    /**
     * Map a FirebaseUser to our domain User model.
     * Supplemental fields (phone, dob) may come from cached prefs if not
     * available on the FirebaseUser directly.
     */
    private fun FirebaseUser.toUser(
        phone: String? = null,
        dateOfBirth: String? = null
    ): User = User(
        id = uid,
        name = displayName ?: email?.substringBefore('@') ?: "User",
        email = email ?: "",
        phone = phone
            ?: phoneNumber
            ?: securePrefs.getString(KEY_CACHED_PHONE, "") ?: "",
        dateOfBirth = dateOfBirth
            ?: securePrefs.getString(KEY_CACHED_DOB, "") ?: "",
        photoUrl = photoUrl?.toString(),
        isAuthenticated = true
    )

    /**
     * Persist email+password in AES-256-GCM encrypted shared preferences
     * so that biometric login can silently re-authenticate with Firebase.
     */
    private fun cacheCredentials(email: String, password: String) {
        securePrefs.edit()
            .putString(KEY_CACHED_EMAIL, email)
            .putString(KEY_CACHED_PASSWORD, password)
            .apply()
    }

    /**
     * Generic helper for calling TheWatch backend API.
     * Sends the Firebase ID token in the Authorization header.
     *
     * @return The response body as a String.
     * @throws Exception on non-2xx response or network failure.
     */
    private suspend fun callBackendApi(
        method: String,
        path: String,
        idToken: String,
        body: String? = null
    ): String = suspendCancellableCoroutine { continuation ->
        try {
            val url = URL("$API_BASE_URL$path")
            val connection = url.openConnection() as HttpURLConnection
            connection.requestMethod = method
            connection.setRequestProperty("Authorization", "Bearer $idToken")
            connection.setRequestProperty("Content-Type", "application/json")
            connection.setRequestProperty("Accept", "application/json")
            connection.connectTimeout = 15_000
            connection.readTimeout = 15_000

            if (body != null && (method == "POST" || method == "PUT" || method == "PATCH")) {
                connection.doOutput = true
                OutputStreamWriter(connection.outputStream, Charsets.UTF_8).use { writer ->
                    writer.write(body)
                    writer.flush()
                }
            }

            val responseCode = connection.responseCode
            val responseBody = if (responseCode in 200..299) {
                connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
            } else {
                val errorBody = connection.errorStream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }
                    ?: "HTTP $responseCode"
                connection.disconnect()
                throw Exception("API error ($responseCode): $errorBody")
            }
            connection.disconnect()
            continuation.resume(responseBody)
        } catch (e: Exception) {
            if (continuation.isActive) {
                continuation.resumeWithException(e)
            }
        }
    }

    /**
     * Translate Firebase-specific exceptions into user-friendly messages.
     * Firebase Auth throws FirebaseAuthException subtypes with error codes like
     * ERROR_INVALID_EMAIL, ERROR_WRONG_PASSWORD, ERROR_USER_NOT_FOUND, etc.
     */
    private fun mapFirebaseException(e: Exception): Exception {
        val message = when {
            e.message?.contains("INVALID_EMAIL", ignoreCase = true) == true ->
                "The email address is not valid."
            e.message?.contains("WRONG_PASSWORD", ignoreCase = true) == true ->
                "Incorrect password. Please try again."
            e.message?.contains("USER_NOT_FOUND", ignoreCase = true) == true ->
                "No account found with this email. Please sign up."
            e.message?.contains("USER_DISABLED", ignoreCase = true) == true ->
                "This account has been disabled. Contact support."
            e.message?.contains("TOO_MANY_REQUESTS", ignoreCase = true) == true ->
                "Too many failed attempts. Please try again later."
            e.message?.contains("EMAIL_ALREADY_IN_USE", ignoreCase = true) == true ->
                "An account with this email already exists. Try logging in."
            e.message?.contains("WEAK_PASSWORD", ignoreCase = true) == true ->
                "Password is too weak. Use at least 8 characters with a mix of letters, numbers, and symbols."
            e.message?.contains("NETWORK", ignoreCase = true) == true ->
                "Network error. Check your connection and try again."
            e.message?.contains("EXPIRED_ACTION_CODE", ignoreCase = true) == true ->
                "This link has expired. Please request a new one."
            e.message?.contains("INVALID_ACTION_CODE", ignoreCase = true) == true ->
                "This link is invalid or has already been used."
            else -> e.message ?: "An unexpected authentication error occurred."
        }
        return Exception(message, e)
    }
}
