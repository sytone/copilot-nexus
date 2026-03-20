# Issue 002: Lifecycle loop script failed to parse

## Symptoms

- Running `scripts/Exercise-VersionedLifecycleLoop.ps1` failed immediately with:
  - `Variable reference is not valid. ':' was not followed by a valid variable name character.`

## Root cause

- A double-quoted interpolated string used `$Context:` directly.
- In PowerShell, `:` immediately after a variable name is parsed as part of the variable token unless the variable is explicitly delimited.

## Fix

- Updated the string interpolation to `${Context}:` in the diagnostics error message.

## Expected result

- The script parses and proceeds to lifecycle execution instead of failing at load time.
