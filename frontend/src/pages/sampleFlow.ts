import type { TestFlow } from '../api/client'

// A hand-authored flow standing in for a real Inspect session — used as the Flows/Export
// pages' fallback when nothing was handed off from Inspect (P9 wires that handoff for real;
// this is just what's shown before you've captured your own). It mirrors the Phase 1 sample
// but uses distinct step wording so its bindings don't collide with SampleLogin's — a
// collision is exactly what the validator is meant to reject.
//
// `satisfies TestFlow` (rather than `: TestFlow`) checks the shape while keeping each field's
// literal type (e.g. actionType stays "navigate", not widened to string) - both matter here:
// the check is what catches a typo'd actionType at compile time, and the narrowing is what
// this file was missing before, which is what broke FlowsPage/ExportPage's TestFlow typing.
export const SAMPLE_FLOW = {
  name: 'DemoLogin',
  startUrl: 'https://the-internet.herokuapp.com/login',
  steps: [
    {
      order: 1,
      actionType: 'navigate',
      label: 'I browse to the demo login page',
      pageName: 'DemoLoginPage',
    },
    {
      order: 2,
      actionType: 'type',
      label: 'I supply the demo username',
      inputValue: 'tomsmith',
      pageName: 'DemoLoginPage',
      locatorKey: 'UsernameInput',
      element: {
        tagName: 'input',
        id: 'username',
        candidates: [{ strategy: 'id', value: 'username', score: 100 }],
      },
    },
    {
      order: 3,
      actionType: 'type',
      label: 'I supply the demo password',
      inputValue: 'SuperSecretPassword!',
      pageName: 'DemoLoginPage',
      locatorKey: 'PasswordInput',
      element: {
        tagName: 'input',
        id: 'password',
        candidates: [{ strategy: 'id', value: 'password', score: 100 }],
      },
    },
    {
      order: 4,
      actionType: 'click',
      label: 'I press the demo login button',
      pageName: 'DemoLoginPage',
      locatorKey: 'LoginButton',
      element: {
        tagName: 'button',
        candidates: [{ strategy: 'css', value: "button[type='submit']", score: 70 }],
      },
    },
    {
      order: 5,
      actionType: 'assertText',
      label: 'I should reach the demo secure area',
      expectedText: 'You logged into a secure area',
      pageName: 'DemoLoginPage',
      locatorKey: 'FlashMessage',
      element: {
        tagName: 'div',
        id: 'flash',
        candidates: [{ strategy: 'id', value: 'flash', score: 100 }],
      },
    },
  ],
} satisfies TestFlow
