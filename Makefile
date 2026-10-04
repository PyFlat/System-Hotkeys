PROJECT  := src/SystemHotkeys
MANIFEST := $(PROJECT)/manifest.json

STATE    := $(PROJECT)/.macrodeck-dev-state

UTF8     := $(if $(filter Windows_NT,$(OS)),chcp.com 65001 >/dev/null &&)
RUN      := $(UTF8) macrodeck-plugin run --project $(PROJECT) --state-directory $(STATE)

# The SDK version, for keeping the macrodeck-plugin CLI in step. Read inside recipes rather than with
# $(shell): GnuWin32's make 3.81 sometimes runs $(shell) with an empty command line.
SDK      := grep -o 'MacroDeck.Sdk" Version="[^"]*' Directory.Packages.props | cut -d'"' -f3
TESTS    := dotnet test SystemHotkeys.slnx --configuration Release

# Shared by pack and release. release must not call $(MAKE): make runs such a line even under -n.
PACK     := rm -f artifacts/*.macroDeckPlugin && \
            macrodeck-plugin build --source $(PROJECT) --output ./artifacts && \
            macrodeck-plugin inspect --artifact "$$(ls artifacts/*.macroDeckPlugin)"

.DEFAULT_GOAL := help
.PHONY: help cli build test run watch stub pack conformance update release

help:
	@echo "make cli            install/update the macrodeck-plugin CLI to the SDK version ($$($(SDK)))"
	@echo "make build          build the solution"
	@echo "make test           unit tests, as the release workflow runs them"
	@echo "make run            run the plugin against the running Macro Deck"
	@echo "make watch          the same, with hot reload / restart on every saved change"
	@echo "make stub           run the plugin against a disposable stub host (no Macro Deck needed)"
	@echo "make pack           build the win-x64 .macroDeckPlugin into artifacts/ and inspect it"
	@echo "make conformance    run the conformance suite, report in conformance.md"
	@echo "make update         bump every package to its newest release (review the diff)"
	@echo "make release [VERSION=x.y.z]"
	@echo "                    test + pack, bump manifest.json only if VERSION differs, tag, push"

cli:
	dotnet tool update --global MacroDeck.Plugin.Cli --version "$$($(SDK))"

build:
	dotnet build SystemHotkeys.slnx

test:
	$(TESTS)

run:
	$(RUN)

watch:
	$(RUN) --watch

stub:
	$(UTF8) macrodeck-plugin run --project $(PROJECT) --stub-host

pack:
	$(PACK)

conformance:
	macrodeck-plugin test --project $(PROJECT) --report markdown --output conformance.md

update:
	dotnet package update

# Pushing the tag starts .github/workflows/release.yml, which checks the manifest version against the
# tag, creates the GitHub release and publishes to the Creator Portal. Without VERSION the manifest's own
# version is released as is; a VERSION that differs is bumped first. Everything that can fail runs before
# the bump commit, so a failed check leaves nothing to undo.
# Versions are ordered by git and awk: under cmd, sort can be Windows' sort.exe, which has no -V.
release:
	@set -e; \
	git pull --ff-only; \
	current="$$(sed -n 's/^  "version": "\(.*\)",$$/\1/p' $(MANIFEST))"; \
	version="$(VERSION)"; [ -n "$$version" ] || version="$$current"; \
	case "$$version" in \
	  [0-9]*.[0-9]*.[0-9]*) ;; \
	  *) echo "usage: make release [VERSION=x.y.z] (manifest: $$current)"; exit 1 ;; \
	esac; \
	test "$$(git rev-parse --abbrev-ref HEAD)" = main || { echo "release from main only"; exit 1; }; \
	test -z "$$(git status --porcelain)" || { echo "working tree is not clean"; exit 1; }; \
	git fetch --tags --quiet origin; \
	! git rev-parse -q --verify "refs/tags/v$$version" >/dev/null || { echo "tag v$$version already exists"; exit 1; }; \
	latest="$$(git tag -l 'v[0-9]*' --sort=v:refname | tail -n 1 | sed 's/^v//')"; \
	newer="$$(awk -v a="$$version" -v b="$$latest" 'BEGIN { split(a, x, "."); split(b, y, "."); for (i = 1; i <= 3; i++) if (x[i] + 0 != y[i] + 0) { print (x[i] + 0 > y[i] + 0 ? "yes" : "no"); exit } print "no" }')"; \
	if [ -n "$$latest" ] && [ "$$newer" != yes ]; then \
	  echo "v$$version is not newer than the latest release v$$latest"; exit 1; \
	fi; \
	echo "releasing v$$version (latest release: v$${latest:-none}, manifest: $$current)"; \
	$(TESTS); \
	$(PACK); \
	if [ "$$version" != "$$current" ]; then \
	  sed -i.bak 's/^  "version": ".*",$$/  "version": "'"$$version"'",/' $(MANIFEST); rm -f $(MANIFEST).bak; \
	  grep -q "^  \"version\": \"$$version\",$$" $(MANIFEST) || { echo "could not set the version in $(MANIFEST)"; git checkout -- $(MANIFEST); exit 1; }; \
	  git commit -m "chore: bump version to $$version" -- $(MANIFEST); \
	fi; \
	git tag "v$$version"; \
	git push --atomic origin main "v$$version"
