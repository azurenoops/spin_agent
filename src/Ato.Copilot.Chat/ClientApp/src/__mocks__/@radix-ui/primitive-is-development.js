// Jest stub for @radix-ui/primitive/is-development.
// The real export uses a "development" condition that Jest's resolver doesn't support.
// In test we always return false (production behavior).
module.exports = { isDevelopment: false };
