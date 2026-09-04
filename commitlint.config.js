// Conventional Commits, with the scope repurposed to carry the task id
// (e.g. "P1-T07") instead of a module name, per CLAUDE.md's commit convention.
module.exports = {
    extends: ["@commitlint/config-conventional"],
    rules: {
        "header-max-length": [2, "always", 120],
        "scope-case": [0],
        "subject-case": [0]
    }
};
