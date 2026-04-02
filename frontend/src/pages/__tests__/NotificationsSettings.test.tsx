import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders, userEvent } from '../../test/test-utils';
import NotificationsSettings from '../settings/NotificationsSettings';
import { apiGet, apiPost, apiPut, apiDelete } from '../../utils/api';

vi.mock('../../utils/api', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiPut: vi.fn(),
  apiDelete: vi.fn(),
}));

vi.mock('../../components/TagSelector', () => ({
  default: () => <div data-testid="tag-selector" />,
}));

function mockResponse(body: unknown, ok = true) {
  return {
    ok,
    json: vi.fn().mockResolvedValue(body),
  } as unknown as Response;
}

describe('NotificationsSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    vi.mocked(apiGet).mockImplementation(async (url: string) => {
      if (url === '/api/notification') {
        return mockResponse([]);
      }

      return mockResponse([]);
    });

    vi.mocked(apiPost).mockResolvedValue(mockResponse({}));
    vi.mocked(apiPut).mockResolvedValue(mockResponse({}));
    vi.mocked(apiDelete).mockResolvedValue(mockResponse({}));
  });

  it('renders the missing SMTP controls and saves them in configJson', async () => {
    const user = userEvent.setup();

    renderWithProviders(<NotificationsSettings />);

    await screen.findByText('Your Notifications');

    await user.click(screen.getByRole('button', { name: /add notification/i }));
    await user.click(screen.getByRole('button', { name: /email \(smtp\)/i }));

    const smtpUsernameInput = screen.getByLabelText(/smtp username/i);
    const smtpPasswordInput = screen.getByLabelText(/smtp password/i);
    const sslToggle = screen.getByLabelText(/use ssl \/ tls/i);

    expect(smtpUsernameInput).toBeInTheDocument();
    expect(smtpPasswordInput).toBeInTheDocument();
    expect(sslToggle).toBeChecked();

    await user.type(screen.getByLabelText(/smtp server/i), 'smtp.example.com');
    await user.type(smtpUsernameInput, 'mailer');
    await user.type(smtpPasswordInput, 'secret');
    await user.type(screen.getByLabelText(/^from/i), 'sportarr@example.com');
    await user.type(screen.getByLabelText(/^to/i), 'user@example.com');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() => {
      expect(apiPost).toHaveBeenCalledWith('/api/notification', expect.any(Object));
    });

    const payload = vi.mocked(apiPost).mock.calls[0][1] as { configJson: string };
    const config = JSON.parse(payload.configJson);

    expect(config.username).toBe('mailer');
    expect(config.password).toBe('secret');
    expect(config.useSsl).toBe(true);
  });

  it('renders media-server path mapping fields and persists them in configJson', async () => {
    const user = userEvent.setup();

    renderWithProviders(<NotificationsSettings />);

    await screen.findByText('Your Notifications');

    await user.click(screen.getByRole('button', { name: /add notification/i }));
    await user.click(screen.getByRole('button', { name: /plex media server/i }));

    const sportarrPathInput = screen.getByLabelText(/sportarr path map/i);
    const serverPathInput = screen.getByLabelText(/server path map/i);

    expect(sportarrPathInput).toBeInTheDocument();
    expect(serverPathInput).toBeInTheDocument();

    await user.type(screen.getByLabelText(/^host/i), 'http://plex.local:32400');
    await user.type(screen.getByLabelText(/api key/i), 'plex-token');
    await user.type(sportarrPathInput, '/downloads/sportarr');
    await user.type(serverPathInput, '/media/library');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() => {
      expect(apiPost).toHaveBeenCalledWith('/api/notification', expect.any(Object));
    });

    const payload = vi.mocked(apiPost).mock.calls[0][1] as { configJson: string };
    const config = JSON.parse(payload.configJson);

    expect(config.pathMapFrom).toBe('/downloads/sportarr');
    expect(config.pathMapTo).toBe('/media/library');
  });
});
