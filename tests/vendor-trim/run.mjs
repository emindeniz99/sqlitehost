#!/usr/bin/env node
// vendor-trim — pins the invariant that unity/vendor.mjs relies on: each
// authoring profile, trimmed to just its own files + the shared engine +
// abstractions, still compiles as ONE assembly (mirroring the UPM package's
// SqliteHost.asmdef). If someone adds a cross-profile reference, or a new
// profile file that vendor.mjs's PROFILE_FILES map doesn't know about, one of
// these builds fails here instead of in a vendor's project.

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { PROFILES, PROFILE_FILES, excludedRelPaths, vendor } from "../../unity/vendor.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const packageDir = path.join(here, "..", "..", "unity", "com.sqlitehost.runtime");

function findDotnet() {
  for (const candidate of ["dotnet", "/opt/dotnet/dotnet"]) {
    try {
      execFileSync(candidate, ["--version"], { stdio: "ignore" });
      return candidate;
    } catch {
      /* try next */
    }
  }
  throw new Error("dotnet SDK not found");
}

const CSPROJ = `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <RootNamespace>SqliteHost</RootNamespace>
    <Nullable>disable</Nullable>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Runtime/**/*.cs" />
  </ItemGroup>
</Project>
`;

const dotnet = findDotnet();
let failures = 0;

for (const profile of PROFILES) {
  const outDir = fs.mkdtempSync(path.join(os.tmpdir(), `vendor-${profile}-`));
  try {
    const { copied } = vendor({ profile, outDir, packageDir, includeSamples: false });

    // The trim must drop every OTHER profile's files and keep this one's.
    for (const rel of excludedRelPaths(profile)) {
      if (fs.existsSync(path.join(outDir, rel))) {
        console.error(`FAIL ${profile}: expected ${rel} to be trimmed but it is present`);
        failures++;
      }
    }
    for (const file of PROFILE_FILES[profile]) {
      if (!fs.existsSync(path.join(outDir, "Runtime", "Runtime", file))) {
        console.error(`FAIL ${profile}: kept-profile file ${file} is missing from the trim`);
        failures++;
      }
    }

    fs.writeFileSync(path.join(outDir, "trim.csproj"), CSPROJ);
    execFileSync(dotnet, ["build", path.join(outDir, "trim.csproj"), "-v", "q", "-nologo"], {
      stdio: "pipe",
    });
    console.log(`ok  ${profile}-only trim compiles as one assembly (${copied} files)`);
  } catch (err) {
    failures++;
    const detail = err.stdout ? err.stdout.toString() : err.message;
    console.error(`FAIL ${profile}: ${detail}`);
  } finally {
    fs.rmSync(outDir, { recursive: true, force: true });
  }
}

if (failures > 0) {
  console.error(`\nVENDOR-TRIM FAILED (${failures} problem(s))`);
  process.exit(1);
}
console.log(`\nVENDOR-TRIM GREEN (${PROFILES.length} profiles trimmed + compiled)`);
