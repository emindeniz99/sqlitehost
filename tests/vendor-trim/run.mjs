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

// Walk .cs files under a dir, returning [relPath, content] pairs.
function csFiles(dir, base = dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...csFiles(full, base));
    else if (entry.name.endsWith(".cs")) out.push([path.relative(base, full), fs.readFileSync(full, "utf8")]);
  }
  return out;
}

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
let compiled = 0;

for (const profile of PROFILES) {
  for (const slim of [false, true]) {
    const label = `${profile}${slim ? " (slim)" : ""}`;
    const outDir = fs.mkdtempSync(path.join(os.tmpdir(), `vendor-${profile}-`));
    try {
      const { copied } = vendor({ profile, outDir, packageDir, includeSamples: false, slim });

      // The trim must drop every OTHER profile's files and keep this one's.
      for (const rel of excludedRelPaths(profile)) {
        if (fs.existsSync(path.join(outDir, rel))) {
          console.error(`FAIL ${label}: expected ${rel} to be trimmed but it is present`);
          failures++;
        }
      }
      for (const file of PROFILE_FILES[profile]) {
        if (!fs.existsSync(path.join(outDir, "Runtime", "Runtime", file))) {
          console.error(`FAIL ${label}: kept-profile file ${file} is missing`);
          failures++;
        }
      }
      if (slim) {
        // No validation source and no residual SLIM guards may survive.
        if (fs.existsSync(path.join(outDir, "Runtime", "Runtime", "SqlParameterScanner.cs"))) {
          console.error(`FAIL ${label}: SqlParameterScanner.cs must be dropped under --slim`);
          failures++;
        }
        for (const [rel, content] of csFiles(outDir)) {
          if (content.includes("#if !SQLITEHOST_SLIM")) {
            console.error(`FAIL ${label}: residual "#if !SQLITEHOST_SLIM" in ${rel}`);
            failures++;
          }
        }
      }

      fs.writeFileSync(path.join(outDir, "trim.csproj"), CSPROJ);
      execFileSync(dotnet, ["build", path.join(outDir, "trim.csproj"), "-v", "q", "-nologo"], {
        stdio: "pipe",
      });
      compiled++;
      console.log(`ok  ${label} trim compiles as one assembly (${copied} files)`);
    } catch (err) {
      failures++;
      const detail = err.stdout ? err.stdout.toString() : err.message;
      console.error(`FAIL ${label}: ${detail}`);
    } finally {
      fs.rmSync(outDir, { recursive: true, force: true });
    }
  }
}

if (failures > 0) {
  console.error(`\nVENDOR-TRIM FAILED (${failures} problem(s))`);
  process.exit(1);
}
console.log(`\nVENDOR-TRIM GREEN (${compiled} profile×mode trims compiled)`);
