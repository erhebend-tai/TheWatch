/**
 * WRITE-AHEAD LOG | File: EULAManagementScreen.kt | Purpose: EULA management with diff, re-accept, version tracking
 * Created: 2026-03-24 | Author: Claude | Deps: Compose Material3, ViewModel
 * Usage: composable("eula_management") { EULAManagementScreen(navController) }
 * NOTE: Blocking flow - user MUST accept updated EULA to continue using app.
 */
package com.thewatch.app.ui.screens.eula

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.navigation.NavController
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

data class EULAVersion(val version: String, val publishedAt: String, val content: String, val changesSummary: List<String> = emptyList())
data class EULAAcceptance(val version: String, val acceptedAt: String, val userId: String)
data class EULAState(val currentVersion: EULAVersion? = null, val acceptedVersion: String? = null, val needsReAccept: Boolean = false, val diffSummary: List<String> = emptyList(), val history: List<EULAAcceptance> = emptyList(), val isLoading: Boolean = true, val isAccepting: Boolean = false, val error: String? = null)

class EULAManagementViewModel : ViewModel() {
    private val _state = MutableStateFlow(EULAState())
    val state: StateFlow<EULAState> = _state.asStateFlow()
    init { viewModelScope.launch { delay(500); _state.value = EULAState(
        currentVersion = EULAVersion("2.1.0", "2026-03-20", EULA_TEXT, listOf("Data portability rights (GDPR Art.20)", "Retention 180->90 days", "Volunteer liability protections", "Account deletion 30-day grace", "H3 geohash disclosure")),
        acceptedVersion = "2.0.0", needsReAccept = true,
        diffSummary = listOf("Sec 4.2: Data portability expanded", "Sec 7.1: Retention reduced", "Sec 9.3: Volunteer liability", "Sec 11: Deletion grace period", "Sec 12.5: H3 geohash"),
        history = listOf(EULAAcceptance("2.0.0", "2026-02-15T10:30:00Z", "user-001"), EULAAcceptance("1.0.0", "2026-01-15T10:34:00Z", "user-001")),
        isLoading = false
    ) } }
    fun accept() { viewModelScope.launch { _state.value = _state.value.copy(isAccepting = true); delay(800); val v = _state.value.currentVersion?.version ?: return@launch; _state.value = _state.value.copy(acceptedVersion = v, needsReAccept = false, isAccepting = false, history = listOf(EULAAcceptance(v, "2026-03-24T12:00:00Z", "user-001")) + _state.value.history) } }
    fun decline() { _state.value = _state.value.copy(error = "You must accept the updated EULA to continue using TheWatch.") }
}

@Composable
fun EULAManagementScreen(navController: NavController, viewModel: EULAManagementViewModel = androidx.lifecycle.viewmodel.compose.viewModel()) {
    val state by viewModel.state.collectAsState()
    Column(modifier = Modifier.fillMaxSize().background(White).verticalScroll(rememberScrollState())) {
        Row(modifier = Modifier.fillMaxWidth().background(Navy).padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { navController.navigateUp() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back", tint = White) }
            Text("EULA & Terms", fontSize = 20.sp, fontWeight = FontWeight.Bold, color = White, modifier = Modifier.weight(1f))
        }
        Column(modifier = Modifier.padding(24.dp)) {
            if (state.needsReAccept) {
                Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFFFFF3E0)), shape = RoundedCornerShape(12.dp)) {
                    Column(Modifier.padding(16.dp)) {
                        Text("EULA Update Required", fontWeight = FontWeight.Bold, fontSize = 16.sp, color = Color(0xFFE65100), modifier = Modifier.semantics { heading() })
                        Spacer(Modifier.height(8.dp))
                        Text("Updated from v${state.acceptedVersion} to v${state.currentVersion?.version}. Review changes below.", fontSize = 13.sp, color = Color(0xFF795548))
                    }
                }
                Spacer(Modifier.height(16.dp))
                Text("What Changed", fontWeight = FontWeight.Bold, fontSize = 16.sp, color = Navy)
                Spacer(Modifier.height(8.dp))
                state.diffSummary.forEach { Row(Modifier.padding(vertical = 4.dp)) { Text("+ ", color = Color(0xFF4CAF50), fontWeight = FontWeight.Bold); Text(it, fontSize = 13.sp, color = Color.DarkGray) } }
                Spacer(Modifier.height(16.dp))
            }
            Text("Current EULA (v${state.currentVersion?.version ?: "..."})", fontWeight = FontWeight.Bold, fontSize = 16.sp, color = Navy)
            Spacer(Modifier.height(8.dp))
            Card(Modifier.fillMaxWidth().height(300.dp), colors = CardDefaults.cardColors(containerColor = Color(0xFFF5F5F5)), shape = RoundedCornerShape(8.dp)) {
                Column(Modifier.padding(12.dp).verticalScroll(rememberScrollState())) { Text(state.currentVersion?.content ?: "Loading...", fontSize = 12.sp, color = Color.DarkGray, lineHeight = 18.sp) }
            }
            Spacer(Modifier.height(16.dp))
            state.error?.let { Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFFFFEBEE)), shape = RoundedCornerShape(8.dp)) { Text(it, fontSize = 13.sp, color = RedPrimary, modifier = Modifier.padding(12.dp)) }; Spacer(Modifier.height(12.dp)) }
            if (state.needsReAccept) {
                Button(onClick = { viewModel.accept() }, Modifier.fillMaxWidth(), enabled = !state.isAccepting, colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))) { Text(if (state.isAccepting) "Accepting..." else "I Accept the Updated EULA", color = White, modifier = Modifier.padding(8.dp)) }
                Spacer(Modifier.height(8.dp))
                OutlinedButton(onClick = { viewModel.decline() }, Modifier.fillMaxWidth()) { Text("Decline", color = RedPrimary) }
            }
            Spacer(Modifier.height(24.dp))
            Text("Acceptance History", fontWeight = FontWeight.Bold, fontSize = 16.sp, color = Navy)
            Spacer(Modifier.height(8.dp))
            state.history.forEach { a -> Row(Modifier.fillMaxWidth().background(Color(0xFFF5F5F5), RoundedCornerShape(8.dp)).padding(12.dp), verticalAlignment = Alignment.CenterVertically) { Column(Modifier.weight(1f)) { Text("Version ${a.version}", fontWeight = FontWeight.Bold, fontSize = 14.sp, color = Navy); Text("Accepted: ${a.acceptedAt}", fontSize = 12.sp, color = Color.Gray) } }; Spacer(Modifier.height(8.dp)) }
        }
    }
}

private val EULA_TEXT = """END USER LICENSE AGREEMENT (EULA) - TheWatch Safety Platform
Version 2.1.0 | Effective Date: March 20, 2026

1. ACCEPTANCE OF TERMS - By downloading, installing, or using TheWatch, you agree to this EULA.
2. SERVICE - Community safety/emergency response: phrase detection, location tracking, volunteer coordination.
3. DATA COLLECTION - Location (always-on), voice/speech, motion, profile, contacts, incident history. Per GDPR Art.6(1)(a).
4. DATA PORTABILITY - Art.15 access, Art.20 portable format (JSON), Art.17 erasure with 30-day grace.
5. VOLUNTEERS - Must be 18+. Not replacement for 911. Good Samaritan protections where applicable.
6. RETENTION - Local: 7 days. Cloud: 90 days. Incidents: per legal requirements.
7. LOCATION - "Always" permission required. H3 geohash indexed for volunteer lookup.
8. DELETION - Request anytime. 30-day grace. Irreversible after grace period.
9. GOVERNING LAW - GDPR (EU), CCPA (CA), LGPD (Brazil), PIPA (Korea), PIPEDA (Canada).
10. CONTACT - legal@thewatch.app"""
