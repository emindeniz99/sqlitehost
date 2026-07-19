#!/usr/bin/env node
import { runDemo } from "../dist/cli.js";

process.exitCode = runDemo(process.argv.slice(2), (line) => console.log(line));
