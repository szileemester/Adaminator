import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  Button,
  Checkbox,
  FormControlLabel,
  MenuItem,
  Stack,
  TextField,
} from '@mui/material';
import type { MatchFormat, TournamentInput, TournamentType } from '../api/types';
import { matchFormatLabels, tournamentTypeLabels } from '../api/types';

const schema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(200, 'Name is too long'),
  date: z.string().min(1, 'Date is required'),
  notes: z.string().max(2000, 'Notes are too long').optional(),
  type: z.enum(['SingleElimination', 'DoubleElimination', 'RoundRobin']),
  defaultMatchFormat: z.enum(['Bo1', 'Bo3', 'Bo5', 'Bo7']),
  thirdPlaceEnabled: z.boolean(),
});

export type TournamentFormValues = z.infer<typeof schema>;

const today = () => new Date().toISOString().slice(0, 10);

const matchFormats: MatchFormat[] = ['Bo1', 'Bo3', 'Bo5', 'Bo7'];
const tournamentTypes: TournamentType[] = ['SingleElimination', 'DoubleElimination', 'RoundRobin'];

interface TournamentFormProps {
  initialValues?: Partial<TournamentFormValues>;
  submitLabel: string;
  submitting?: boolean;
  onSubmit: (values: TournamentInput) => void;
  onCancel?: () => void;
}

export function TournamentForm({
  initialValues,
  submitLabel,
  submitting = false,
  onSubmit,
  onCancel,
}: TournamentFormProps) {
  const {
    register,
    handleSubmit,
    control,
    watch,
    setValue,
    formState: { errors },
  } = useForm<TournamentFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: '',
      date: today(),
      notes: '',
      type: 'SingleElimination',
      defaultMatchFormat: 'Bo3',
      thirdPlaceEnabled: false,
      ...initialValues,
    },
  });

  const selectedType = watch('type');
  const isSingleElimination = selectedType === 'SingleElimination';

  // Third Place Match is Single-Elimination only; clear it when switching away from it.
  useEffect(() => {
    if (!isSingleElimination) {
      setValue('thirdPlaceEnabled', false);
    }
  }, [isSingleElimination, setValue]);

  const submit = handleSubmit((values) => {
    onSubmit({
      name: values.name.trim(),
      date: values.date,
      notes: values.notes?.trim() ? values.notes.trim() : null,
      type: values.type,
      defaultMatchFormat: values.defaultMatchFormat,
      thirdPlaceEnabled: values.type === 'SingleElimination' && values.thirdPlaceEnabled,
    });
  });

  return (
    <form onSubmit={submit} noValidate>
      <Stack spacing={3}>
        <TextField
          label="Name"
          required
          {...register('name')}
          error={Boolean(errors.name)}
          helperText={errors.name?.message}
        />

        <TextField
          label="Date"
          type="date"
          required
          slotProps={{ inputLabel: { shrink: true } }}
          {...register('date')}
          error={Boolean(errors.date)}
          helperText={errors.date?.message}
        />

        <TextField
          label="Notes"
          multiline
          minRows={3}
          {...register('notes')}
          error={Boolean(errors.notes)}
          helperText={errors.notes?.message}
        />

        <Controller
          name="type"
          control={control}
          render={({ field }) => (
            <TextField select label="Tournament type" {...field}>
              {tournamentTypes.map((type) => (
                <MenuItem key={type} value={type}>
                  {tournamentTypeLabels[type]}
                </MenuItem>
              ))}
            </TextField>
          )}
        />

        <Controller
          name="defaultMatchFormat"
          control={control}
          render={({ field }) => (
            <TextField select label="Default match format" {...field}>
              {matchFormats.map((format) => (
                <MenuItem key={format} value={format}>
                  {matchFormatLabels[format]}
                </MenuItem>
              ))}
            </TextField>
          )}
        />

        <Controller
          name="thirdPlaceEnabled"
          control={control}
          render={({ field }) => (
            <FormControlLabel
              control={<Checkbox checked={field.value} onChange={field.onChange} disabled={!isSingleElimination} />}
              label="Third place match (Single Elimination only)"
            />
          )}
        />

        <Stack direction="row" spacing={2}>
          <Button type="submit" variant="contained" disabled={submitting}>
            {submitLabel}
          </Button>
          {onCancel && (
            <Button variant="text" onClick={onCancel} disabled={submitting}>
              Cancel
            </Button>
          )}
        </Stack>
      </Stack>
    </form>
  );
}
