// ═══════════════════════════════════════════════════════════════════════════════
// TheWatch — TeamCity Pipeline Configuration (Kotlin DSL)
// ═══════════════════════════════════════════════════════════════════════════════
//
// Mirrors the GitHub Actions CI pipeline exactly:
//   1. Restore → 2. Build → 3. Unit Tests → 4. Audit Integrity → 5. Publish
//   + Integration Tests (separate build config, triggered on master)
//   + Security Scan
//   + Deploy (chained after CI passes)
//
// Uses the same build.cmd / build.sh scripts as GitHub Actions for parity.
//
// Requirements:
//   - TeamCity agent with .NET 10 SDK
//   - Agent labels: "dotnet10", "windows" or "linux"
//   - VCS root connected to https://github.com/erhebend-tai/TheWatch.git
// ═══════════════════════════════════════════════════════════════════════════════

import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildFeatures.*
import jetbrains.buildServer.configs.kotlin.buildSteps.*
import jetbrains.buildServer.configs.kotlin.triggers.*
import jetbrains.buildServer.configs.kotlin.vcs.*

version = "2024.12"

project {
    description = "TheWatch Life-Safety Emergency Response Platform"

    vcsRoot(TheWatchVcs)

    buildType(Build)
    buildType(IntegrationTests)
    buildType(SecurityScan)
    buildType(Deploy)

    // Build chain: Build → IntegrationTests → Deploy
    sequential {
        buildType(Build)
        parallel {
            buildType(IntegrationTests)
            buildType(SecurityScan)
        }
        buildType(Deploy)
    }

    params {
        param("env.DOTNET_NOLOGO", "true")
        param("env.DOTNET_CLI_TELEMETRY_OPTOUT", "true")
        param("system.CONFIGURATION", "Release")
        param("system.SOLUTION", "TheWatch.slnx")
    }
}

// ── VCS Root ────────────────────────────────────────────────────────────────

object TheWatchVcs : GitVcsRoot({
    name = "TheWatch GitHub"
    url = "https://github.com/erhebend-tai/TheWatch.git"
    branch = "refs/heads/master"
    branchSpec = """
        +:refs/heads/develop
        +:refs/heads/feature/*
        +:refs/heads/release/*
        +:refs/pull/*/head
    """.trimIndent()
    authMethod = password {
        userName = "%github.username%"
        password = "%github.token%"
    }
})

// ── Build & Unit Tests ──────────────────────────────────────────────────────

object Build : BuildType({
    name = "Build & Test"
    description = "Restore → Build → Unit Tests → Audit Integrity → Publish"

    vcs {
        root(TheWatchVcs)
    }

    // ── Restore ──────────────────────────────────────────────
    steps {
        script {
            name = "Restore"
            scriptContent = """
                if [ -f build.sh ]; then
                    chmod +x build.sh && ./build.sh restore
                else
                    build.cmd restore
                fi
            """.trimIndent()
        }

        // ── Build ────────────────────────────────────────────────
        script {
            name = "Build"
            scriptContent = """
                if [ -f build.sh ]; then
                    ./build.sh build
                else
                    build.cmd build
                fi
            """.trimIndent()
        }

        // ── Unit Tests ───────────────────────────────────────────
        script {
            name = "Unit Tests"
            scriptContent = """
                if [ -f build.sh ]; then
                    ./build.sh test
                else
                    build.cmd test
                fi
            """.trimIndent()
        }

        // ── Audit Trail Integrity ────────────────────────────────
        script {
            name = "Audit Trail Integrity Check"
            scriptContent = """
                if [ -f build.sh ]; then
                    ./build.sh audit-verify
                else
                    build.cmd audit-verify
                fi
            """.trimIndent()
        }

        // ── Publish ──────────────────────────────────────────────
        script {
            name = "Publish"
            scriptContent = """
                if [ -f build.sh ]; then
                    ./build.sh publish
                else
                    build.cmd publish
                fi
            """.trimIndent()
        }
    }

    triggers {
        vcs {
            branchFilter = "+:*"
        }
    }

    features {
        // Parse .trx test results
        feature {
            type = "xml-report-plugin"
            param("xmlReportParsing.reportType", "trx")
            param("xmlReportParsing.reportDirs", "test-results/**/*.trx")
        }
        // Publish coverage
        feature {
            type = "xml-report-plugin"
            param("xmlReportParsing.reportType", "dotNetCoverage")
            param("xmlReportParsing.reportDirs", "test-results/**/coverage.opencover.xml")
        }
    }

    artifactRules = """
        artifacts/** => artifacts.zip
        test-results/** => test-results.zip
    """.trimIndent()

    requirements {
        exists("DotNetCLI_Path")
    }
})

// ── Integration Tests ───────────────────────────────────────────────────────

object IntegrationTests : BuildType({
    name = "Integration Tests"
    description = "Azure OpenAI swarm agent integration tests"

    vcs {
        root(TheWatchVcs)
    }

    steps {
        script {
            name = "Restore & Build"
            scriptContent = """
                if [ -f build.sh ]; then
                    chmod +x build.sh
                    ./build.sh restore
                    ./build.sh build
                else
                    build.cmd restore
                    build.cmd build
                fi
            """.trimIndent()
        }

        script {
            name = "Integration Tests"
            scriptContent = """
                if [ -f build.sh ]; then
                    ./build.sh test-integ
                else
                    build.cmd test-integ
                fi
            """.trimIndent()
        }
    }

    params {
        password("env.AZURE_OPENAI_API_KEY", "%azure.openai.apikey%")
        param("env.AZURE_OPENAI_ENDPOINT", "%azure.openai.endpoint%")
        param("env.AZURE_OPENAI_DEPLOYMENT_GPT41", "gpt-4.1")
        param("env.AZURE_OPENAI_DEPLOYMENT_GPT4O", "gpt-4o")
        param("env.AZURE_OPENAI_DEPLOYMENT_GPT4O_MINI", "gpt-4o-mini")
        param("env.AZURE_OPENAI_DEPLOYMENT_EMBEDDING", "text-embedding-3-large")
    }

    triggers {
        // Only run on master/main pushes (expensive — calls live Azure OpenAI)
        vcs {
            branchFilter = """
                +:<default>
                +:refs/heads/main
            """.trimIndent()
        }
    }

    features {
        feature {
            type = "xml-report-plugin"
            param("xmlReportParsing.reportType", "trx")
            param("xmlReportParsing.reportDirs", "test-results/**/*.trx")
        }
    }

    dependencies {
        snapshot(Build) {
            onDependencyFailure = FailureAction.FAIL_TO_START
        }
    }

    requirements {
        exists("DotNetCLI_Path")
    }
})

// ── Security Scan ───────────────────────────────────────────────────────────

object SecurityScan : BuildType({
    name = "Security Scan"
    description = "NuGet vulnerability check and secrets scan"

    vcs {
        root(TheWatchVcs)
    }

    steps {
        script {
            name = "NuGet Vulnerability Check"
            scriptContent = """
                dotnet restore TheWatch.slnx --verbosity minimal
                dotnet list TheWatch.slnx package --vulnerable --include-transitive
            """.trimIndent()
        }

        script {
            name = "Secrets Scan"
            scriptContent = """
                echo "Scanning for hardcoded secrets..."
                grep -rn --include="*.cs" --include="*.json" --include="*.yaml" \
                    -E "(password|secret|apikey|api_key|connectionstring)\s*[:=]\s*['\"][^'\"]{8,}" \
                    --exclude-dir=obj --exclude-dir=bin --exclude-dir=.git \
                    . && echo "##teamcity[buildProblem description='Potential hardcoded secrets found']" || echo "No secrets found."
            """.trimIndent()
        }
    }

    dependencies {
        snapshot(Build) {
            onDependencyFailure = FailureAction.FAIL_TO_START
        }
    }
})

// ── Deploy ──────────────────────────────────────────────────────────────────

object Deploy : BuildType({
    name = "Deploy to Azure"
    description = "Deploy infrastructure (Bicep) and applications to Azure"

    vcs {
        root(TheWatchVcs)
    }

    steps {
        script {
            name = "Azure Login"
            scriptContent = """
                az login --service-principal -u "%azure.sp.appid%" -p "%azure.sp.password%" --tenant "%azure.sp.tenant%"
                az account set --subscription "%azure.subscription.id%"
            """.trimIndent()
        }

        script {
            name = "Deploy Infrastructure (Bicep)"
            scriptContent = """
                az deployment group create \
                    --resource-group "%azure.resource.group%" \
                    --template-file infra/main.bicep \
                    --parameters infra/main.parameters.json \
                    --parameters "sqlAdminPassword=%azure.sql.password%" \
                    --parameters "rabbitPassword=%azure.rabbit.password%" \
                    --name "thewatch-tc-%build.number%"
            """.trimIndent()
        }

        script {
            name = "Deploy Applications"
            scriptContent = """
                if [ -f build.sh ]; then
                    chmod +x build.sh
                    ./build.sh restore
                    ./build.sh build
                    ./build.sh publish
                else
                    build.cmd restore
                    build.cmd build
                    build.cmd publish
                fi

                az webapp deploy --resource-group "%azure.resource.group%" --name "%azure.basename%-api" --src-path artifacts/Dashboard.Api --type zip
                az webapp deploy --resource-group "%azure.resource.group%" --name "%azure.basename%-web" --src-path artifacts/Dashboard.Web --type zip
                az functionapp deployment source config-zip --resource-group "%azure.resource.group%" --name "%azure.basename%-functions" --src artifacts/Functions
            """.trimIndent()
        }
    }

    params {
        password("azure.sp.password", "%azure.sp.password%")
        password("azure.sql.password", "%azure.sql.password%")
        password("azure.rabbit.password", "%azure.rabbit.password%")
        param("azure.sp.appid", "%azure.sp.appid%")
        param("azure.sp.tenant", "%azure.sp.tenant%")
        param("azure.subscription.id", "%azure.subscription.id%")
        param("azure.resource.group", "Watch-Init")
        param("azure.basename", "thewatch")
    }

    triggers {
        // Manual trigger only for deploy (no auto-deploy)
        // To enable auto-deploy on master, uncomment:
        // vcs { branchFilter = "+:<default>" }
    }

    dependencies {
        snapshot(Build) {
            onDependencyFailure = FailureAction.FAIL_TO_START
        }
        snapshot(IntegrationTests) {
            onDependencyFailure = FailureAction.FAIL_TO_START
        }
    }

    requirements {
        exists("DotNetCLI_Path")
    }
})
