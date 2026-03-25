package com.thewatch.app.navigation

import androidx.navigation.NavController
import androidx.navigation.NavGraphBuilder
import androidx.navigation.compose.composable
import androidx.navigation.compose.navigation
import com.thewatch.app.ui.screens.login.LoginScreen
import com.thewatch.app.ui.screens.signup.SignUpScreen
import com.thewatch.app.ui.screens.forgotpassword.ForgotPasswordScreen
import com.thewatch.app.ui.screens.resetpassword.ResetPasswordScreen
import com.thewatch.app.ui.screens.eula.EulaScreen
import com.thewatch.app.ui.screens.twofactor.TwoFactorScreen
import com.thewatch.app.ui.screens.emailverify.EmailVerifyScreen
import com.thewatch.app.ui.screens.home.HomeScreen
import com.thewatch.app.ui.screens.profile.ProfileScreen
import com.thewatch.app.ui.screens.permissions.PermissionsScreen
import com.thewatch.app.ui.screens.history.HistoryScreen
import com.thewatch.app.ui.screens.volunteering.VolunteeringScreen
import com.thewatch.app.ui.screens.contacts.ContactsScreen
import com.thewatch.app.ui.screens.settings.SettingsScreen
import com.thewatch.app.ui.screens.evacuation.EvacuationScreen
import com.thewatch.app.ui.screens.health.HealthDashboardScreen
import com.thewatch.app.ui.screens.health.WearableManagementScreen
import com.thewatch.app.ui.screens.sos.SosCountdownScreen
import com.thewatch.app.services.SosTriggerService
import com.thewatch.app.services.SosTriggerSource

sealed class NavRoute(val route: String) {
    object Login : NavRoute("login")
    object SignUp : NavRoute("signup")
    object ForgotPassword : NavRoute("forgot_password")
    object ResetPassword : NavRoute("reset_password")
    object Eula : NavRoute("eula")
    object Home : NavRoute("home")
    object Profile : NavRoute("profile")
    object Permissions : NavRoute("permissions")
    object History : NavRoute("history")
    object Volunteering : NavRoute("volunteering")
    object Contacts : NavRoute("contacts")
    object Settings : NavRoute("settings")
    object Evacuation : NavRoute("evacuation")
    object HealthDashboard : NavRoute("health_dashboard")
    object WearableManagement : NavRoute("wearable_management")
    object TwoFactor : NavRoute("two_factor")
    object EmailVerify : NavRoute("email_verify")

    object SosCountdown : NavRoute("sos_countdown")

    object AuthGraph : NavRoute("auth_graph")
    object AppGraph : NavRoute("app_graph")
}

fun NavGraphBuilder.authGraph(navController: NavController) {
    navigation(startDestination = NavRoute.Login.route, route = NavRoute.AuthGraph.route) {
        composable(NavRoute.Login.route) {
            LoginScreen(navController = navController)
        }
        composable(NavRoute.SignUp.route) {
            SignUpScreen(navController = navController)
        }
        composable(NavRoute.ForgotPassword.route) {
            ForgotPasswordScreen(navController = navController)
        }
        composable(NavRoute.ResetPassword.route) {
            ResetPasswordScreen(navController = navController)
        }
        composable(NavRoute.Eula.route) {
            EulaScreen(navController = navController)
        }
        composable(NavRoute.TwoFactor.route) {
            TwoFactorScreen(navController = navController)
        }
        composable(NavRoute.EmailVerify.route) {
            EmailVerifyScreen(navController = navController)
        }
    }
}

fun NavGraphBuilder.appGraph(navController: NavController) {
    navigation(startDestination = NavRoute.Home.route, route = NavRoute.AppGraph.route) {
        composable(NavRoute.Home.route) {
            HomeScreen(navController = navController)
        }
        composable(NavRoute.Profile.route) {
            ProfileScreen(navController = navController)
        }
        composable(NavRoute.Permissions.route) {
            PermissionsScreen(navController = navController)
        }
        composable(NavRoute.History.route) {
            HistoryScreen(navController = navController)
        }
        composable(NavRoute.Volunteering.route) {
            VolunteeringScreen(navController = navController)
        }
        composable(NavRoute.Contacts.route) {
            ContactsScreen(navController = navController)
        }
        composable(NavRoute.Settings.route) {
            SettingsScreen(navController = navController)
        }
        composable(NavRoute.Evacuation.route) {
            EvacuationScreen(navController = navController)
        }
        composable(NavRoute.HealthDashboard.route) {
            HealthDashboardScreen(navController = navController)
        }
        composable(NavRoute.WearableManagement.route) {
            WearableManagementScreen(navController = navController)
        }
        composable(NavRoute.SosCountdown.route) {
            // SosTriggerService is injected via Hilt at the Activity level.
            // The screen obtains it from the Hilt entry point or LocalContext.
            // For now, we use a simplified approach — the service is a singleton.
            SosCountdownScreen(
                sosTriggerService = SosTriggerService.instance,
                triggerSource = SosTriggerSource.MANUAL_BUTTON,
                onDismiss = { navController.popBackStack() }
            )
        }
    }
}

fun NavController.navigateToAuth() {
    graph.startDestinationRoute?.let { route ->
        navigate(route) {
            popUpTo(graph.id) { inclusive = true }
        }
    }
}

fun NavController.navigateToApp() {
    navigate(NavRoute.AppGraph.route) {
        popUpTo(graph.id) { inclusive = true }
    }
}
