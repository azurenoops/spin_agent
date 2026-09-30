import { useContext, useMemo } from 'react';
import { DEFAULT_SETTINGS, SettingsContext } from './useSettings';

type DisplayDate = string | Date | null | undefined;

/** Display only: callers retain original timestamps for records, audit, and exports. */
export function useDateFormatter() {
  const settings = useContext(SettingsContext)?.settings ?? DEFAULT_SETTINGS;
  return useMemo(() => {
    let timezone = settings.timezone;
    try {
      new Intl.DateTimeFormat('en-US', { timeZone: timezone });
    } catch (error) {
      if (!(error instanceof RangeError)) throw error;
      console.warn('Invalid display time zone; using UTC.');
      timezone = 'UTC';
    }
    const dateOptions: Intl.DateTimeFormatOptions = { year: 'numeric', month: '2-digit', day: '2-digit' };
    const dates = new Intl.DateTimeFormat('en-US', { ...dateOptions, timeZone: timezone });
    const calendarDates = new Intl.DateTimeFormat('en-US', { ...dateOptions, timeZone: 'UTC' });
    const times = new Intl.DateTimeFormat('en-GB', {
      hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
      timeZone: timezone, timeZoneName: 'short',
    });
    const parse = (value: DisplayDate) => {
      if (!value) return null;
      const date = value instanceof Date ? value : new Date(value);
      return Number.isNaN(date.getTime()) ? null : date;
    };
    const dateText = (date: Date, calendar: boolean) => {
      const parts = (calendar ? calendarDates : dates).formatToParts(date);
      const part = (type: string) => parts.find(value => value.type === type)!.value;
      const year = part('year'), month = part('month'), day = part('day');
      if (settings.dateFormat === 'ISO') return `${year}-${month}-${day}`;
      return settings.dateFormat === 'EU' ? `${day}/${month}/${year}` : `${month}/${day}/${year}`;
    };
    const formatDate = (value: DisplayDate, calendar = false) => {
      const date = parse(value);
      // Date-only fields are calendar days, not instants in the user's time zone.
      return date ? dateText(date, calendar || typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) : '—';
    };
    return {
      formatDate,
      formatCalendarDate: (value: DisplayDate) => formatDate(value, true),
      formatDateTime: (value: DisplayDate) => {
        const date = parse(value);
        return date ? `${dateText(date, false)} ${times.format(date)}` : '—';
      },
    };
  }, [settings.dateFormat, settings.timezone]);
}
